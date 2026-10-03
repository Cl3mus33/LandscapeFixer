"""Assemble publish/LandscapeFixer-<version>-win-x64 (+ .zip) from the native Release build.

    python scripts/package.py 0.1.0

Run after building native/buildRelease (see README). Strips debug symbols, keeps the layout
Nexus users install as one mod: the exe, LandscapeFixerTexTools.dll, the .NET runtime folder
(LandscapeFixer_dotnetlib) and the translations.
"""
import os
import shutil
import sys
import zipfile

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
version = sys.argv[1] if len(sys.argv) > 1 else '0.0.0'
src = os.path.join(root, 'native', 'buildRelease', 'bin')
name = f'LandscapeFixer-{version}-win-x64'
dst = os.path.join(root, 'publish', name)
if os.path.exists(dst):
    shutil.rmtree(dst)
shutil.copytree(src, dst)
for dirpath, _, files in os.walk(dst):
    for f in files:
        if f.lower().endswith('.pdb'):
            os.remove(os.path.join(dirpath, f))

zip_path = dst + '.zip'
if os.path.exists(zip_path):
    os.remove(zip_path)
count = 0
with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
    for dirpath, _, files in os.walk(dst):
        for f in sorted(files):
            full = os.path.join(dirpath, f)
            zf.write(full, os.path.relpath(full, dst).replace(os.sep, '/'))
            count += 1
print(f'{zip_path}: {count} files, {os.path.getsize(zip_path):,} bytes')
