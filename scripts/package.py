"""Assemble publish/LandscapeFixer (+ LandscapeFixer-<version>-win-x64.zip and .7z).

    python scripts/package.py 2.0.2

Needs the texture tools built once (native/buildRelease, see README). The window is published as a
self-contained single-file exe (no .NET install needed), next to the DirectXTex wrapper, its OpenMP runtime
and the translations - the layout installed as one mod in a mod manager.

The folder is always called LandscapeFixer (the .7z has it as its root folder, the .zip is flat): a version in
the folder name would break the executable path set in the mod manager at every update.
"""
import os
import shutil
import subprocess
import sys
import zipfile

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
version = sys.argv[1] if len(sys.argv) > 1 else '0.0.0'
name = f'LandscapeFixer-{version}-win-x64'
dst = os.path.join(root, 'publish', 'LandscapeFixer')
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
# MO2 only recognises an archive as a mod when it holds a known file type: this one-word ini makes it install as a mod.
with open(os.path.join(dst, 'LandscapeFixer.ini'), 'w', newline='') as f:
    f.write('Empty')
shutil.rmtree(staging)

zip_path = os.path.join(root, 'publish', name + '.zip')
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

seven_zip = shutil.which('7z') or next((p for p in ('C:/Program Files/7-Zip/7z.exe', 'C:/Program Files (x86)/7-Zip/7z.exe') if os.path.exists(p)), None)
if seven_zip:
    sevenz_path = os.path.join(root, 'publish', name + '.7z')
    if os.path.exists(sevenz_path):
        os.remove(sevenz_path)
    subprocess.run([seven_zip, 'a', '-t7z', '-mx=9', sevenz_path, 'LandscapeFixer'], cwd=os.path.join(root, 'publish'), check=True, stdout=subprocess.DEVNULL)
    print(f'{sevenz_path}: {os.path.getsize(sevenz_path):,} bytes')
else:
    print('7-Zip not found: no .7z made')
