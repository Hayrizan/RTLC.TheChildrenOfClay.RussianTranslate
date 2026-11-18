import argparse
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


ROOT_DIR = Path(__file__).resolve().parent.parent
PROJECT_FILE = ROOT_DIR / "src" / "RTLC.TheChildrenOfClay.RussianTranslate" / "RTLC.TheChildrenOfClay.RussianTranslate.csproj"
PLUGIN_NAME = "RTLC.TheChildrenOfClay.RussianTranslate"


def project_version():
    root = ET.parse(PROJECT_FILE).getroot()
    version = root.findtext(".//Version")
    if not version:
        raise ValueError(f"Version was not found in {PROJECT_FILE}")
    return version


def main():
    parser = argparse.ArgumentParser(description="Package the plugin and localized texture archives.")
    parser.add_argument("--version", default=project_version(), help="Release version (defaults to the project version).")
    parser.add_argument("--output-dir", type=Path, default=ROOT_DIR / "dist", help="Directory for the ZIP files.")
    parser.add_argument(
        "--dll",
        type=Path,
        default=ROOT_DIR / "src" / PLUGIN_NAME / "bin" / "Release" / "netstandard2.1" / f"{PLUGIN_NAME}.dll",
        help="Built plugin DLL path.",
    )
    args = parser.parse_args()

    bundle = ROOT_DIR / "Assets" / "rtlc.bundle"
    readme = ROOT_DIR / "README.md"
    textures = sorted((ROOT_DIR / "Assets" / "Textures").glob("*.png"))
    required_files = [args.dll, bundle, readme]
    missing = [path for path in required_files if not path.is_file()]
    if missing:
        parser.error("required file(s) not found: " + ", ".join(str(path) for path in missing))
    if len(textures) != 14:
        parser.error(f"expected 14 localized PNG files, found {len(textures)}")

    safe_version = args.version.strip()
    if safe_version.startswith("v"):
        safe_version = safe_version[1:]
    if not safe_version or any(char not in "0123456789.-+" and not char.isalpha() for char in safe_version):
        parser.error(f"invalid release version: {args.version}")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    plugin_zip = args.output_dir / f"{PLUGIN_NAME}-v{safe_version}.zip"
    textures_zip = args.output_dir / "RTLC.TheChildrenOfClay.Textures" / f"RTLC.TheChildrenOfClay.Textures-v{safe_version}.zip"
    textures_zip.parent.mkdir(parents=True, exist_ok=True)
    plugin_folder = f"BepInEx/plugins/{PLUGIN_NAME}"

    with ZipFile(plugin_zip, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
        archive.write(args.dll, f"{plugin_folder}/{PLUGIN_NAME}.dll")
        archive.write(bundle, f"{plugin_folder}/rtlc.bundle")
        archive.write(readme, f"{plugin_folder}/README.md")

    with ZipFile(textures_zip, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr(
            "README.txt",
            "The Children of Clay Russian localization textures.\n"
            "This archive contains the 14 localized PNG files from the source repository.\n"
            "The plugin ZIP already includes these textures inside rtlc.bundle; this archive is optional.\n",
        )
        for texture in textures:
            archive.write(texture, f"Textures/{texture.name}")

    print(f"Created {plugin_zip} ({plugin_zip.stat().st_size} bytes)")
    print(f"Created {textures_zip} ({textures_zip.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
