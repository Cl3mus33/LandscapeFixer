"""Assemble publish/LandscapeFixer-<version>-win-x64 (+ .zip).

    python scripts/package.py 0.1.0

Needs the texture tools built once (native/buildRelease, see README). The window is published as a
self-contained single-file exe (no .NET install needed), next to the DirectXTex wrapper, its OpenMP runtime
and the translations - the layout installed as one mod in a mod manager.
"""
import os
import shutil
import subprocess
import sys
import zipfile

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
version = sys.argv[1] if len(sys.argv) > 1 else '0.0.0'
name = f'LandscapeFixer-{version}-win-x64'
dst = os.path.join(root, 'publish', name)
staging = os.path.join(root, 'publish', '_app')
tools = os.path.join(root, 'native', 'buildRelease', 'bin', 'LandscapeFixer_dotnetlib')

for needed in ('LandscapeFixerTexTools.dll', 'vcomp140.dll'):
    if not os.path.exists(os.path.join(tools, needed)):
        sys.exit(f'{needed} not found in {tools}: build native/ first (see README).')

for folder in (dst, staging):
    if os.path.exists(folder):
        shutil.rmtree(folder)

subprocess.run(
    ['dotnet', 'publish', os.path.join(root, 'src', 'LandscapeFixer.App', 'LandscapeFixer.App.csproj'),
     '-c', 'Release', '-r', 'win-x64', '--self-contained',
     '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
     '-p:DebugType=None', '-p:DebugSymbols=false', f'-p:Version={version.split("-")[0]}', '-o', staging],
    check=True)

os.makedirs(dst)
shutil.copy2(os.path.join(staging, 'LandscapeFixer.exe'), dst)
shutil.copytree(os.path.join(staging, 'LandscapeFixer_translations'), os.path.join(dst, 'LandscapeFixer_translations'))
for needed in ('LandscapeFixerTexTools.dll', 'vcomp140.dll'):
    shutil.copy2(os.path.join(tools, needed), dst)
shutil.rmtree(staging)

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
