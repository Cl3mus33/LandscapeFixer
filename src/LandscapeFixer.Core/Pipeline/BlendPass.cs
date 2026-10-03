using System.Threading;
using System.Threading.Tasks;
using LandscapeFixer.Core.Configuration;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using nifly;

namespace LandscapeFixer.Core.Pipeline;

public sealed record BlendPassResult(int RecordsScanned, int MeshesPatchedInPlace, int TextureSetsCreated, int AlternateTexturesAssigned);

/// <summary>
/// The blend pass (formerly AutoBlend): for every landscape mesh whose textures have a statics/blend
/// sibling, flips alpha test to alpha blend IN PLACE at the mesh's own vanilla path (the load-order
/// winner, patched into the output folder) and gives the records that show such a texture a derived,
/// vanilla-looking TextureSet through an Alternate Texture. PBR conversion is left to PG Patcher.
/// </summary>
public sealed class BlendPass
{
    private enum RecordKind
    {
        Static,
        MoveableStatic,
    }

    private enum ShapeTreatmentKind
    {
        /// <summary>Neither this record's own Alternate Texture (if any) nor the mesh's own
        /// embedded default resolves to a statics/blending sibling - leave this shape alone.</summary>
        Untouched,

        /// <summary>The mesh's own embedded (vanilla, no-override) diffuse resolves - handled
        /// identically to AltTexDerived from here on: a TXST is derived from the mesh's own
        /// embedded slots and assigned via a brand-new Alternate Texture, exactly as if some other
        /// mod had already shipped that override. Never bakes anything into the mesh itself - only
        /// the alpha-blend mode is ever mesh-level, and that's uniform across every record sharing
        /// this mesh regardless of kind (see the alpha-flip pass in Run()).</summary>
        BaseDerived,

        /// <summary>This specific record already carries an Alternate Texture on this shape, and
        /// THAT texture set's Diffuse resolves - record-specific, independent of what the mesh's
        /// own embedded default is.</summary>
        AltTexDerived,

        /// <summary>A mod-provided "*_autoblend_exceptions.json" (see ModProvidedMeshConfigReader)
        /// forces this mesh into blend mode even though nothing was actually detected for it -
        /// mesh-level alpha-mode flip only, same as BaseDerived/AltTexDerived for the purposes of
        /// the shared-mesh alpha flip pass in Run(), but never gets a derived TextureSet/Alternate
        /// Texture (there is no Source/Detection to derive one from) - explicitly skipped in that
        /// part of Run() instead.</summary>
        ForcedBlend,
    }

    /// <summary>One shape with NiAlphaProperty found on a mesh, plus what its own embedded
    /// (no-override) diffuse resolves to, if anything, and whether that shape's alpha mode is
    /// already blend (vs. test) - used to recognize a mesh some OTHER mod already fully converted
    /// itself (see the "already alpha-blended" check in ClassifyRecord).</summary>
    private sealed record AlphaShape(int ShapeIndex, string EmbeddedDiffuse, LandscapeFolderDetection? EmbeddedDetection, SourceTexturePaths EmbeddedSlots, bool AlreadyAlphaBlended);

    /// <summary>What one record wants done to one shape of a shared mesh.</summary>
    private sealed record ShapeTreatment(ShapeTreatmentKind Kind, int ShapeIndex, LandscapeFolderDetection? Detection, SourceTexturePaths? Source);


    private readonly ExtractSettings _settings;
    private readonly PassContext _ctx;

    public BlendPass(PassContext ctx)
    {
        _ctx = ctx;
        _settings = ctx.Settings;
    }

    public BlendPassResult Run()
    {
        var readerWarnings = new List<string>();
        // Mod authors can ship their own "*_autoblend.json" allowlist entries alongside their mod
        // (e.g. a landscape pack declaring which of its own textures are safe to auto-generate a
        // statics/blend sibling for) - collected from every enabled mod, not just the user's own
        // locally-configured AutoGenerateAllowlist, and merged together.
        var modProvidedAllowlist = ModProvidedAllowlistReader.Collect(_ctx.Mo2Reader, _ctx.DataFolder, readerWarnings);
        var effectiveAllowlist = _settings.AutoGenerateAllowlist
            .Concat(modProvidedAllowlist)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (modProvidedAllowlist.Count > 0)
        {
            _ctx.Report($"Merged {modProvidedAllowlist.Count} mod-provided allowlist entry/entries from *_autoblend.json file(s).");
        }

        // Mod authors can also ship precise per-mesh overrides (Blend/Test) alongside their mod in
        // a "*_autoblend_exceptions.json" file - see ModProvidedMeshConfigReader's own remarks for why
        // this is a flat list of exceptions rather than a file-wide default.
        var meshOverrides = ModProvidedMeshConfigReader.Collect(_ctx.Mo2Reader, _ctx.DataFolder, readerWarnings);
        if (meshOverrides.Count > 0)
        {
            _ctx.Report($"Loaded {meshOverrides.Count} mod-provided mesh override(s) from *_autoblend_exceptions.json file(s).");
        }

        MissingTextureGenerator? textureGenerator = null;
        if (_settings.AutoGenerateMissingStatics)
        {
            textureGenerator = new MissingTextureGenerator(_ctx.FileProbe, _ctx.OutputFolder, _settings.GameType == GameType.SkyrimLE);
        }

        var folderDetector = new LandscapeFolderDetector(
            _settings.LandscapeFolderRules, _ctx.FileProbe, textureGenerator, effectiveAllowlist, _settings.GeneratePbrSlots);
        var blacklist = new BlacklistEvaluator(_settings.BlendMeshBlacklist, _settings.BlendEditorIdBlacklistKeywords);

        // Runs before any mesh/record scanning, as its own explicit phase: the allowlist is a
        // small, fixed, known-upfront list (unlike everything else here, which is only discovered
        // by scanning), so there's no reason to wait for a mesh to lazily reference one of these
        // textures before generating it. Detect() itself does the actual exists-check/generate -
        // MissingTextureGenerator's own per-target-path cache means the per-mesh scan below hitting
        // the exact same textures later is then just a fast cache lookup, not a second attempt.
        if (textureGenerator is not null)
        {
            for (var i = 0; i < effectiveAllowlist.Count; i++)
            {
                _ctx.Report($"Generating missing statics textures... ({i + 1}/{effectiveAllowlist.Count})", i + 1, effectiveAllowlist.Count);
                try
                {
                    folderDetector.Detect(effectiveAllowlist[i]);
                }
                catch (Exception ex)
                {
                    // One allowlist entry's own detection must not abort every other entry (and the
                    // whole run) - matches the same resilience pattern used everywhere else here.
                    // Reported directly: an unrelated corrupt archive elsewhere in the real Data
                    // folder reliably crashed generation the very first time any Detect() call
                    // needed to check it, before this loop (or the file probes it calls into) had
                    // any protection against a single bad archive.
                    _ctx.AddDiagnostic($"Could not check '{effectiveAllowlist[i]}' for a missing statics texture, skipped: {ex.Message}");
                }
            }
        }
        var txstFactory = new DerivedTextureSetFactory(_ctx.OutputMod, _settings.TextureSetNamingTemplate);
        var derivedTxstCache = new Dictionary<string, TextureSet?>(StringComparer.OrdinalIgnoreCase);

        _ctx.Report("Scanning Static and MoveableStatic records...");
        var meshIndex = new MeshUsageIndex();
        var candidatesByMesh = new Dictionary<string, List<FormKey>>(StringComparer.OrdinalIgnoreCase);
        var recordKinds = new Dictionary<FormKey, RecordKind>();
        var recordsScanned = 0;

        void ScanRecord(FormKey formKey, string? editorId, string? meshPath, RecordKind kind)
        {
            recordsScanned++;
            if (string.IsNullOrEmpty(meshPath))
            {
                return;
            }

            recordKinds[formKey] = kind;

            var isBlacklisted = blacklist.IsMeshBlacklisted(meshPath) || blacklist.IsEditorIdBlacklisted(editorId ?? string.Empty)
                || !_ctx.Scope.IsInScope(meshPath);
            meshIndex.Record(meshPath, formKey, isBlacklisted);

            if (isBlacklisted)
            {
                return;
            }

            if (!candidatesByMesh.TryGetValue(meshPath, out var list))
            {
                list = new List<FormKey>();
                candidatesByMesh[meshPath] = list;
            }
            list.Add(formKey);
        }

        // The actual root cause behind repeated "[ERROR] Absolute path did not have Data folder
        // within it." reports, finally pinned down: Model.File is itself a Mutagen AssetLink -
        // reading .GivenPath on a record whose OWN mesh path is a malformed absolute path (some
        // mod author's CK setup, same class of authoring mistake as the malformed texture paths
        // fixed in 1.0.4/1.0.5) throws this exact exception, for ANY Static/MoveableStatic record
        // in the ENTIRE load order - not just ones AutoBlend cares about - and it happens here,
        // during this initial scan, well before the per-mesh loop 1.0.5 wrapped. Confirmed directly:
        // assigning a Model a malformed absolute File path and reading GivenPath back throws the
        // identical exception. One bad record from an unrelated mod must not abort scanning every
        // other record.
        foreach (var stat in _ctx.Env.LoadOrder.PriorityOrder.WinningOverrides<IStaticGetter>())
        {
            try
            {
                ScanRecord(stat.FormKey, stat.EditorID, stat.Model?.File.GivenPath, RecordKind.Static);
            }
            catch (Exception ex)
            {
                _ctx.AddDiagnostic($"Skipped Static record {stat.FormKey} - could not read its own mesh path: {ex.Message}");
            }
        }

        foreach (var mstt in _ctx.Env.LoadOrder.PriorityOrder.WinningOverrides<IMoveableStaticGetter>())
        {
            try
            {
                ScanRecord(mstt.FormKey, mstt.EditorID, mstt.Model?.File.GivenPath, RecordKind.MoveableStatic);
            }
            catch (Exception ex)
            {
                _ctx.AddDiagnostic($"Skipped MoveableStatic record {mstt.FormKey} - could not read its own mesh path: {ex.Message}");
            }
        }

        _ctx.Report($"{recordsScanned} Static/MoveableStatic record(s) scanned, {candidatesByMesh.Count} distinct mesh(es) not blacklisted.");

        var tempDir = Path.Combine(NifIo.GetAsciiTempRoot(), "LandscapeFixer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var meshesPatchedInPlace = 0;
        var altTexAssigned = 0;

        // Two locks, not one: warningsLock guards only the (rare, error-path-only) shared warnings
        // list, while espLock guards the ESP-mutation tail below (_ctx.OutputMod/derivedTxstCache/
        // altTexAssigned - Mutagen mod objects are not safe for concurrent mutation). Keeping them
        // separate means a warning raised during the genuinely parallel scan/classify/patch work
        // never has to wait behind whatever mesh currently holds the ESP lock.
        var espLock = new object();

        try
        {
            var totalMeshes = candidatesByMesh.Count;
            var meshCounter = 0;

            // Per-mesh work - extraction, nifly parse, classification, alpha-flip patch+save - has
            // no shared mutable state (each mesh gets its own temp file, own NifFile, own local
            // dictionaries) except for the handful of things now explicitly locked above/below, so
            // it runs across every available core instead of one mesh at a time. This was the
            // dominant cost of a real run (disk extraction + nifly parse + nifly save, per mesh,
            // over thousands of meshes) sitting entirely on one thread while the rest of the
            // machine's cores were idle - reported directly as generation feeling "a bit slow" on
            // large modlists.
            Parallel.ForEach(candidatesByMesh, kvp =>
            {
                var (meshPath, candidateFormKeys) = kvp;
                var currentCount = Interlocked.Increment(ref meshCounter);
                try
                {
                // Texture generation happens lazily inside folderDetector.Detect() below, as part
                // of this same per-mesh pass - folding its running count into this same status line
                // (rather than a separate phase) is what makes it visible during the run at all,
                // since the native/WPF shells only ever surface _ctx.Report()'s live Status text, not
                // the final PatchRunResult, until the run completes.
                var textureGenSuffix = textureGenerator is not null
                    ? $" - {textureGenerator.GeneratedCount} texture(s) generated"
                    : string.Empty;
                _ctx.Report($"Patching meshes... ({currentCount}/{totalMeshes}){textureGenSuffix}", currentCount, totalMeshes);

                var plan = meshIndex.GetPlan(meshPath);
                if (!plan.ShouldPatch)
                {
                    return;
                }

                var meshRelativeToData = Path.Combine("meshes", meshPath);
                string extractedPath;
                try
                {
                    extractedPath = ExtractToTemp(_ctx.FileProbe, meshRelativeToData, tempDir);
                }
                catch (FileNotFoundException)
                {
                    _ctx.AddDiagnostic($"Mesh not found, skipped: {meshPath}");
                    return;
                }

                // Every shape with NiAlphaProperty, regardless of whether the mesh's OWN embedded
                // (no-override) diffuse resolves - a shape whose default doesn't match can still be
                // in scope for a specific record via that record's own Alternate Texture (see
                // ClassifyRecord below), so filtering here would silently drop those records before
                // their AltTex is ever even looked at.
                // Kept alive (not disposed) for the rest of this mesh's own try block, down through
                // the NiAlphaBlendPatcher.Patch(...) call below - that used to open and parse this
                // exact same file a second time from scratch via its own sourcePath argument, a full
                // duplicate binary nifly parse of every mesh that needs patching for no data reuse.
                // Passing the already-loaded NifFile through directly halves the nifly parse cost of
                // this whole loop.
                var alphaShapes = new Dictionary<string, AlphaShape>(StringComparer.OrdinalIgnoreCase);
                using var readNif = new NifFile();
                if (NifIo.Load(readNif, extractedPath) != 0)
                {
                    _ctx.AddDiagnostic($"nifly failed to load, skipped: {meshPath}");
                    return;
                }

                var allShapes = readNif.GetShapes();
                for (var shapeIndex = 0; shapeIndex < allShapes.Count; shapeIndex++)
                {
                    var shape = allShapes[shapeIndex];
                    if (!shape.HasAlphaProperty())
                    {
                        continue;
                    }

                    var slots = NifShapeTextureResolver.GetAllSlots(readNif, shape);
                    if (string.IsNullOrEmpty(slots.Diffuse))
                    {
                        continue;
                    }

                    var alreadyAlphaBlended = false;
                    var alphaProperty = readNif.GetAlphaProperty(shape);
                    if (alphaProperty is not null)
                    {
                        alreadyAlphaBlended = NiAlphaFlags.IsAlphaBlendEnabled(alphaProperty.flags)
                            && !NiAlphaFlags.IsAlphaTestEnabled(alphaProperty.flags);
                    }

                    var detection = folderDetector.Detect(slots.Diffuse);
                    alphaShapes[shape.name.get()] = new AlphaShape(shapeIndex, slots.Diffuse, detection, slots, alreadyAlphaBlended);
                }

                if (alphaShapes.Count == 0)
                {
                    return;
                }

                // Classify every candidate record's per-shape treatment: does IT (via its own
                // Alternate Texture, or failing that the mesh's own embedded default) end up
                // displaying a texture with a statics/blending sibling? This has to be evaluated per
                // record because two records sharing this exact mesh can carry completely different
                // Alternate Textures on the same shape (e.g. one region's variant has a statics
                // match, another region's variant does not) - the mesh's own embedded default is
                // only what a record with NO override on that shape falls back to.
                var perRecordTreatment = new Dictionary<FormKey, Dictionary<string, ShapeTreatment>>();
                foreach (var formKey in candidateFormKeys)
                {
                    perRecordTreatment[formKey] = ClassifyRecord(formKey, recordKinds[formKey], alphaShapes, _ctx.Env, folderDetector, _ctx.AddDiagnostic);
                }

                // A mod-provided *_autoblend_exceptions.json entry for this exact mesh path overrides
                // whatever every record's own classification above just decided - Test forces every
                // shape back to Untouched (the precise-exclusion case), Blend upgrades any shape
                // that's still Untouched to ForcedBlend (a shape ClassifyRecord already derived a
                // real texture for is left as-is - it's already blending, nothing to force).
                if (meshOverrides.TryGetValue(meshRelativeToData, out var meshOverride))
                {
                    foreach (var treatment in perRecordTreatment.Values)
                    {
                        foreach (var shapeName in treatment.Keys.ToList())
                        {
                            var current = treatment[shapeName];
                            treatment[shapeName] = meshOverride switch
                            {
                                MeshOverrideSetting.Test => current with { Kind = ShapeTreatmentKind.Untouched, Detection = null, Source = null },
                                MeshOverrideSetting.Blend when current.Kind == ShapeTreatmentKind.Untouched =>
                                    current with { Kind = ShapeTreatmentKind.ForcedBlend },
                                _ => current,
                            };
                        }
                    }
                }

                // Every shape treatment (BaseDerived - the mesh's own embedded default resolves - or
                // AltTexDerived - a specific record's existing Alternate Texture resolves) gets
                // handled identically from here on: derive/reuse a TXST and assign it via a new
                // Alternate Texture, exactly like an existing AltTex override always did. Neither
                // kind ever bakes a diffuse into the mesh itself anymore, so the ONLY thing the
                // physical file needs is the SAME alpha-mode flip for every shape that has AT LEAST
                // ONE non-Untouched treatment across ANY record sharing it - uniform, mesh-level,
                // never record-specific. That means every mesh needs at most ONE physical output
                // (patched in place, per plan.PatchInPlace - or the one blacklist-mixing duplicate
                // MeshUsageIndex already computed) - never a second, third, etc. "_blendN" variant.
                var shapesNeedingAlphaFlip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var treatment in perRecordTreatment.Values)
                {
                    foreach (var (shapeName, t) in treatment)
                    {
                        if (t.Kind != ShapeTreatmentKind.Untouched)
                        {
                            shapesNeedingAlphaFlip.Add(shapeName);
                        }
                    }
                }

                if (shapesNeedingAlphaFlip.Count == 0)
                {
                    return;
                }

                // For every in-scope shape whose OWN embedded default resolves, ALSO rewrite its raw
                // diffuse slot to the vanilla-looking "blend" identity (VanillaDerivedDiffusePath) -
                // safe to do unconditionally now, unlike before the ESP always drives every record's
                // treatment: any record with its own Alternate Texture overrides this raw slot
                // completely at runtime regardless of what's baked here, so the SAME value can be
                // written once for the whole mesh without creating any per-record divergence (no
                // duplication needed for this). This still matters for two real cases: (1) a record
                // with NO override of its own renders straight from this slot, so it needs to be the
                // real "blend" texture, not the original blend-edge one; (2) PG Patcher discovers and
                // converts meshes by matching a shape's CURRENT diffuse path against its own
                // PBRNifPatcher json entries (which AutoBlend itself authors at this same "blend"
                // identity) - leaving the raw slot at its original, non-blend path would give PG
                // Patcher nothing to match. Shapes with no EmbeddedDetection at all (only reachable
                // via some record's own AltTex, never the mesh's own default) get alpha mode flipped
                // only - there's no sensible "vanilla blend" identity to bake for those.
                //
                // (An earlier variant of this line briefly baked DerivedDiffusePath - the real PBR
                // path - instead, to test whether that helped PG Patcher pick up these shapes. Root
                // caused instead to DerivedTextureSetFactory's own separate "textures\" double-prefix
                // bug - reverted back to VanillaDerivedDiffusePath here once that was confirmed.)
                var alphaConversionByShapeName = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var shapeName in shapesNeedingAlphaFlip)
                {
                    alphaConversionByShapeName[shapeName] = alphaShapes.TryGetValue(shapeName, out var alphaShape) && alphaShape.EmbeddedDetection is not null
                        ? alphaShape.EmbeddedDetection.VanillaDerivedDiffusePath
                        : null;
                }

                // The mesh is patched at its own (vanilla) path, whatever plan says: the version extracted
                // above is the load-order winner (ERM, SMIM, ... or vanilla), and the LandscapeFixer output loads
                // after it, so the patched file simply replaces it - no renamed "_blend" copy for PG Patcher
                // to trip over. A mesh shared with records the blacklist excludes (ice, frozen, cave, ...)
                // used to get such a copy, because the alpha test -> blend flip is mesh-wide and would reach
                // those excluded records too; they now share the flip (their own textures are untouched,
                // and only the in-scope records get an Alternate Texture - those are per record).
                var outputMeshPath = Path.Combine(_ctx.OutputFolder, meshRelativeToData);
                IEnumerable<FormKey> recordsToAssign = plan.PatchInPlace ? perRecordTreatment.Keys : plan.DuplicateForRecords;

                var patcher = new NiAlphaBlendPatcher();
                var patchResult = patcher.Patch(readNif, extractedPath, outputMeshPath, alphaConversionByShapeName);
                if (patchResult is not null)
                {
                    Interlocked.Increment(ref meshesPatchedInPlace);
                }

                // Everything from here down touches _ctx.OutputMod/derivedTxstCache/altTexAssigned - real,
                // shared Mutagen mod state that isn't safe for concurrent mutation - so it's the one
                // part of this mesh's own work that stays serialized behind a lock. It's also the
                // cheapest part (in-memory record creation, no disk I/O, no nifly parsing), so
                // holding this lock costs far less than the parallelism above gains.
                lock (espLock)
                {
                foreach (var formKey in recordsToAssign)
                {
                    if (!perRecordTreatment.TryGetValue(formKey, out var treatment))
                    {
                        continue;
                    }

                    var hasAnyDerived = treatment.Values.Any(t => t.Kind is ShapeTreatmentKind.BaseDerived or ShapeTreatmentKind.AltTexDerived);
                    if (!hasAnyDerived)
                    {
                        // Benefits purely from the patched physical mesh -
                        // nothing needs to change at the ESP level for this record. Also covers a
                        // record whose only treatment is ForcedBlend (a mod-provided override),
                        // which never gets its own Alternate Texture - see the shape loop below.
                        continue;
                    }

                    var model = GetOrCreateOverrideModel(formKey, recordKinds[formKey], _ctx.OutputMod, _ctx.Env, _ctx.AddDiagnostic);
                    if (model is null)
                    {
                        continue;
                    }

                    foreach (var (shapeName, t) in treatment)
                    {
                        if (t.Kind is ShapeTreatmentKind.Untouched or ShapeTreatmentKind.ForcedBlend)
                        {
                            // ForcedBlend (a mod-provided *_autoblend_exceptions.json override) has no
                            // Detection/Source to derive a TextureSet from - the mesh-level alpha
                            // flip pass above already handles it entirely on its own, nothing more
                            // is needed at the ESP level for this shape.
                            continue;
                        }

                        // Keyed on the actual resolved texture path being derived - NOT
                        // t.Source!.SourceName, which for a "BaseDerived" shape (see
                        // ClassifyRecord below) is the NIF SHAPE's own name (e.g. "RockSkirt"),
                        // not the texture's identity. Generic shape names like "RockSkirt" are
                        // reused across many unrelated meshes with completely different diffuse
                        // textures (e.g. MountainTrim01's own "RockSkirt" uses SnowRocks01,
                        // while some other landscape mesh's own "RockSkirt" uses Rocks01) - a
                        // shape-name-keyed cache collided across them, so whichever mesh
                        // happened to be processed first "won" the cache entry and every other
                        // mesh sharing that shape name silently inherited its WRONG derived
                        // TextureSet instead of creating its own. Reproduced directly: a vanilla
                        // + ERM - Enhanced Rocks and Mountains test run assigned the SAME
                        // "Rocks01"-derived TextureSet to every "RockSkirt" shape across
                        // MountainTrim01/02/03 and their Wet variants, even though several of
                        // them embed a different vanilla diffuse. DerivedDiffusePath already
                        // encodes the rule folder in its own path (e.g.
                        // "textures\landscape\blend\snowrocks01.dds"), so it alone is a
                        // sufficient and correct cache key.
                        var cacheKey = t.Detection!.DerivedDiffusePath;
                        if (!derivedTxstCache.TryGetValue(cacheKey, out var derivedTxst))
                        {
                            try
                            {
                                derivedTxst = txstFactory.CreateDerived(t.Source!, t.Detection!);
                            }
                            catch (Exception ex)
                            {
                                // A source TextureSet's own texture path came from a third-party
                                // plugin, unvalidated - some mod authors' CK setups bake in a
                                // malformed path (e.g. an absolute path missing "Data" entirely,
                                // which Mutagen's AssetLink construction rejects outright).
                                // Skipping just this one derived TextureSet (and every shape that
                                // would have used it) keeps one bad mod from aborting the whole
                                // run for every other record - matches the existing "left
                                // untouched"/"skipped" resilience pattern used elsewhere here.
                                _ctx.AddDiagnostic($"Could not create derived TextureSet for '{t.Source!.SourceName}' "
                                    + $"({t.Detection!.Rule.FolderName}): {ex.Message} - left untouched.");
                                derivedTxst = null;
                            }

                            if (derivedTxst is not null && t.Detection!.PbrNormalPath is not null)
                            {
                                // Community Shaders' own PBR material config for the new TXST
                                // record (see MissingTextureGenerator.TryMirrorPbrTextureSetJson) -
                                // keyed by the TextureSet EditorID only, so it carries no pbr\ path
                                // into the plugin or the mesh and leaves the PBR conversion of the
                                // mesh itself to PG Patcher. Only meaningful once PBR generation
                                // actually resolved a sibling for this texture.
                                textureGenerator?.TryMirrorPbrTextureSetJson(t.Source!.SourceName, derivedTxst.EditorID!);
                            }

                            derivedTxstCache[cacheKey] = derivedTxst;
                        }

                        if (derivedTxst is null)
                        {
                            continue;
                        }

                        AlternateTextureAssigner.Assign(model, shapeName, t.ShapeIndex, derivedTxst);
                        altTexAssigned++;
                    }
                }
                } // end lock (espLock)
                }
                catch (Exception ex)
                {
                    // Anything unexpected while processing one mesh's own records (malformed
                    // third-party plugin data Mutagen only actually validates once a record's
                    // field is first touched, a corrupt mesh nifly can't fully recover from, etc.)
                    // must not abort every OTHER mesh's already-computed work. The specific,
                    // predictable failure modes already have their own narrower catches above
                    // (mesh not found, unresolvable existing TextureSet, malformed derived-texture
                    // path); this is the backstop for whatever isn't one of those yet - matches the
                    // same "left untouched"/"skipped" resilience pattern, just at mesh granularity
                    // instead of per-record.
                    _ctx.AddDiagnostic($"Unexpected error processing '{meshPath}', skipped: {ex.Message}");
                }
            });
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }

        if (textureGenerator is not null)
        {
            _ctx.Report($"Auto-generated {textureGenerator.GeneratedCount} missing statics/blending texture(s)"
                + (textureGenerator.FailedCount > 0 ? $" ({textureGenerator.FailedCount} attempt(s) failed - see log)." : "."));
            textureGenerator.WritePbrNifPatcherJson();
            if (textureGenerator.MirroredJsonCount > 0)
            {
                _ctx.Report($"Wrote {textureGenerator.MirroredJsonCount} PBRNifPatcher entry/entries for generated/nested PBR variant(s) "
                    + "into a single combined json.");
            }
            if (textureGenerator.MirroredTextureSetJsonCount > 0)
            {
                _ctx.Report($"Wrote {textureGenerator.MirroredTextureSetJsonCount} PBRTextureSets json config(s) for derived PBR TextureSet(s).");
            }
            _ctx.AddDiagnostics(textureGenerator.Diagnostics);
        }


        _ctx.AddDiagnostics(readerWarnings);
        return new BlendPassResult(recordsScanned, meshesPatchedInPlace, derivedTxstCache.Count, altTexAssigned);
    }

    private static Dictionary<string, ShapeTreatment> ClassifyRecord(
        FormKey formKey,
        RecordKind kind,
        Dictionary<string, AlphaShape> alphaShapes,
        IGameEnvironment env,
        LandscapeFolderDetector folderDetector,
        Action<string> addWarning)
    {
        var result = new Dictionary<string, ShapeTreatment>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<IAlternateTextureGetter>? existingAltTexs = kind switch
        {
            RecordKind.Static => env.LinkCache.TryResolve<IStaticGetter>(formKey, out var s) ? s.Model?.AlternateTextures : null,
            _ => env.LinkCache.TryResolve<IMoveableStaticGetter>(formKey, out var m) ? m.Model?.AlternateTextures : null,
        };

        foreach (var (shapeName, alphaShape) in alphaShapes)
        {
            // AlternateTexture.Name is a CK-authored label that can go stale if the mesh's shapes
            // were ever renamed/reordered after the override was created - Index (the shape's
            // position among this NIF's own shape list) is what the game engine actually resolves
            // by, so it's the authoritative fallback whenever the name doesn't match anything.
            var existingAltTex = existingAltTexs?.FirstOrDefault(a => a.Name == shapeName)
                ?? existingAltTexs?.FirstOrDefault(a => a.Index == alphaShape.ShapeIndex);

            if (existingAltTex is not null)
            {
                var existingTxst = ResolveNonPbrTextureSet(env, existingAltTex.NewTexture.FormKey);
                if (existingTxst is not null)
                {
                    var source = SourceTexturePaths.FromTextureSet(existingTxst);
                    var detection = !string.IsNullOrEmpty(source.Diffuse) ? folderDetector.Detect(source.Diffuse) : null;
                    result[shapeName] = detection is not null
                        ? new ShapeTreatment(ShapeTreatmentKind.AltTexDerived, alphaShape.ShapeIndex, detection, source)
                        : new ShapeTreatment(ShapeTreatmentKind.Untouched, alphaShape.ShapeIndex, null, null);
                }
                else
                {
                    // The Alternate Texture points at a FormKey that doesn't resolve in this
                    // environment (missing/masked plugin) - there is nothing correct to derive, so
                    // leave it untouched rather than guessing from the mesh's unrelated default.
                    addWarning($"Could not resolve existing TextureSet referenced by shape '{shapeName}' on record {formKey} - left untouched.");
                    result[shapeName] = new ShapeTreatment(ShapeTreatmentKind.Untouched, alphaShape.ShapeIndex, null, null);
                }
            }
            else if (alphaShape.EmbeddedDetection is not null)
            {
                // If this shape is ALREADY alpha-blended AND its embedded diffuse already IS the
                // resolved statics/blending/blend variant (Detect() hit the "already inside a rule
                // folder" case, meaning nothing would actually change), there is nothing left for
                // AutoBlend to do - some other mod (e.g. a landscape pack that ships its own
                // pre-blended custom mesh, overriding Model.File directly rather than via an ESP
                // AlternateTexture) already fully did this itself. Duplicating and renaming the
                // mesh anyway (every BaseDerived shape gets a "_blend" physical copy) only risks
                // breaking a downstream tool that expects to still find/patch the ORIGINAL mesh
                // reference - reported directly by a user running PGPatcher after AutoBlend, where
                // AutoBlend's own renamed duplicate (no longer made) was not recognized as patchable and ended
                // up missing whatever PGPatcher would have added. Leaving it Untouched here means
                // the record keeps pointing at whatever mesh it already had - unchanged.
                var alreadyDone = alphaShape.AlreadyAlphaBlended
                    && string.Equals(
                        alphaShape.EmbeddedDiffuse.Replace('/', '\\'),
                        alphaShape.EmbeddedDetection.VanillaDerivedDiffusePath.Replace('/', '\\'),
                        StringComparison.OrdinalIgnoreCase);

                result[shapeName] = alreadyDone
                    ? new ShapeTreatment(ShapeTreatmentKind.Untouched, alphaShape.ShapeIndex, null, null)
                    : new ShapeTreatment(ShapeTreatmentKind.BaseDerived, alphaShape.ShapeIndex, alphaShape.EmbeddedDetection, alphaShape.EmbeddedSlots);
            }
            else
            {
                result[shapeName] = new ShapeTreatment(ShapeTreatmentKind.Untouched, alphaShape.ShapeIndex, null, null);
            }
        }

        return result;
    }

    // A TruePBR pack very often overrides the vanilla TXST records themselves with "textures\pbr\\" paths.
    // What AutoBlend derives from must be the vanilla-looking version (PG Patcher is what makes things PBR -
    // see DerivedTextureSetFactory), so this returns the first TextureSet in the override chain (winning
    // first) whose diffuse is not under "pbr\\"; a retexture mod's non-PBR override is still the winning
    // record itself. Falls back to the winning record when the whole chain is PBR.
    private static ITextureSetGetter? ResolveNonPbrTextureSet(IGameEnvironment env, FormKey formKey)
    {
        // Walked mod by mod, highest priority first: this environment is the untyped flavor, whose
        // LinkCache can't enumerate a record's whole override chain.
        ITextureSetGetter? winning = null;
        foreach (var listing in env.LoadOrder.PriorityOrder)
        {
            if (listing.Mod is not ISkyrimModGetter mod || !mod.TextureSets.TryGetValue(formKey, out var record))
            {
                continue;
            }

            winning ??= record;
            var diffuse = record.Diffuse?.GivenPath;
            if (!string.IsNullOrEmpty(diffuse) && !IsUnderPbrFolder(diffuse))
            {
                return record;
            }
        }

        return winning;
    }

    private static bool IsUnderPbrFolder(string path)
    {
        var p = path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        return p.StartsWith("pbr" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("textures" + Path.DirectorySeparatorChar + "pbr" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static Model? GetOrCreateOverrideModel(FormKey recordFormKey, RecordKind kind, SkyrimMod patchMod, IGameEnvironment env, Action<string> addWarning)
    {
        if (kind == RecordKind.Static)
        {
            if (!env.LinkCache.TryResolve<IStaticGetter>(recordFormKey, out var winning))
            {
                addWarning($"Could not resolve Static record {recordFormKey}, skipped.");
                return null;
            }

            return patchMod.Statics.GetOrAddAsOverride(winning).Model;
        }

        if (!env.LinkCache.TryResolve<IMoveableStaticGetter>(recordFormKey, out var winningMstt))
        {
            addWarning($"Could not resolve MoveableStatic record {recordFormKey}, skipped.");
            return null;
        }

        return patchMod.MoveableStatics.GetOrAddAsOverride(winningMstt).Model;
    }

    private static string ExtractToTemp(IGameFileProbe fileProbe, string relativeDataPath, string tempDir)
    {
        using var source = fileProbe.OpenRead(relativeDataPath);
        var destPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + Path.GetExtension(relativeDataPath));
        using var dest = File.Create(destPath);
        source.CopyTo(dest);
        return destPath;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp extraction folder
        }
    }

    // Builds the Mutagen environment; when an explicit load order was given, a plugin Mutagen can't
    // even open (typically empty/corrupted - see UnreadablePluginFinder) is dropped with a warning
    // and the build retried, instead of one bad file aborting the whole run.
}
