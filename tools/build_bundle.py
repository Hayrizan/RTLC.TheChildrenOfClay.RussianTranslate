import argparse
import os
import sys
import UnityPy
from PIL import Image
from UnityPy.enums import ArchiveFlags, TextureFormat
from UnityPy.export import Texture2DConverter
from UnityPy.files import BundleFile, ObjectReader
from UnityPy.files.SerializedFile import SerializedType
from UnityPy.helpers import Tpk, TypeTreeHelper
from UnityPy.streams import EndianBinaryReader, EndianBinaryWriter

sys.stdout.reconfigure(encoding="utf-8")

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
ASSETS_DIR = os.path.join(ROOT_DIR, "Assets")
RU_JSON_PATH = os.path.join(ASSETS_DIR, "Translations", "ru.json")
TEXTURES_DIR = os.path.join(ASSETS_DIR, "Textures")
OUT_BUNDLE_PATH = os.path.join(ASSETS_DIR, "rtlc.bundle")
GAME_RESOURCES_ASSETS = os.environ.get("THE_CHILDREN_OF_CLAY_RESOURCES_ASSETS")

UNITY_VERSION = "2022.3.62f2"
UNITY_VER_OBJ = Tpk.UnityVersion.from_str(UNITY_VERSION)

# Old type hashes matching Unity 2022.3.62f2 standalone Windows build
CLASS_TYPE_HASHES = {
    142: "3f322e24428f2e487bccecc433f30cb6",  # AssetBundle
    49: "486ba4e15dbd6aea8ac1a064305889c8",   # TextAsset
    28: "31f65ae343f671bf462f15c1b41e9768",   # Texture2D
}


def build_typetree_node(class_id: int) -> Tpk.TypeTreeNode:
    class_info = Tpk.TPKTYPETREE.ClassInformation[class_id].getVersionedClass(UNITY_VER_OBJ)
    if class_info is None or class_info.ReleaseRootNode is None:
        raise RuntimeError(f"Missing TPK ReleaseRootNode for class {class_id}")

    nodes_buf = Tpk.TPKTYPETREE.NodeBuffer
    str_buf = Tpk.TPKTYPETREE.StringBuffer
    stack = [(class_info.ReleaseRootNode, 0)]
    nodes = []
    idx = 0
    while stack:
        nid, level = stack.pop(0)
        nd = nodes_buf[nid]
        nodes.append(
            Tpk.TypeTreeNode(
                m_ByteSize=nd.ByteSize,
                m_Index=idx,
                m_Version=nd.Version,
                m_MetaFlag=nd.MetaFlag,
                m_Level=level,
                m_Type=str_buf[nd.TypeName],
                m_Name=str_buf[nd.Name],
                m_TypeFlags=nd.TypeFlags,
                m_RefTypeHash=0,
            )
        )
        stack = [(child_id, level + 1) for child_id in nd.SubNodes] + stack
        idx += 1
    return Tpk.TypeTreeNode.from_list(nodes)


def make_serialized_type(class_id: int) -> SerializedType:
    st = SerializedType.__new__(SerializedType)
    st.class_id = class_id
    st.is_stripped_type = False
    st.script_type_index = -1
    st.script_id = b"\x00" * 16
    st.old_type_hash = bytes.fromhex(CLASS_TYPE_HASHES[class_id])
    st.node = build_typetree_node(class_id)
    st.type_dependencies = []
    return st


def make_object(sf, path_id: int, type_id: int, class_id: int, st: SerializedType, raw_bytes: bytes) -> ObjectReader:
    obj = ObjectReader.__new__(ObjectReader)
    obj.assets_file = sf
    obj.path_id = path_id
    obj.type_id = type_id
    obj.class_id = class_id
    obj.serialized_type = st
    obj.byte_start = 0
    obj.byte_size = len(raw_bytes)
    obj.reader = EndianBinaryReader(raw_bytes, endian="<")
    obj.data = raw_bytes
    return obj


def serialize_text_asset(st: SerializedType, name: str, text_content: str) -> bytes:
    w = EndianBinaryWriter(endian="<")
    TypeTreeHelper.write_typetree(
        {"m_Name": name, "m_Script": text_content},
        st.node,
        w,
    )
    return w.bytes


def serialize_texture2d(st: SerializedType, name: str, png_path: str) -> bytes:
    with Image.open(png_path) as im:
        rgba = im.convert("RGBA")
        width, height = rgba.size
        raw_pixels, tex_fmt = Texture2DConverter.image_to_texture2d(rgba, TextureFormat.RGBA32, flip=True)

    tex_dict = {
        "m_Name": name,
        "m_ForcedFallbackFormat": 4,
        "m_DownscaleFallback": False,
        "m_IsAlphaChannelOptional": False,
        "m_Width": width,
        "m_Height": height,
        "m_CompleteImageSize": len(raw_pixels),
        "m_MipsStripped": 0,
        "m_TextureFormat": int(tex_fmt),
        "m_MipCount": 1,
        "m_IsReadable": True,
        "m_IsPreProcessed": False,
        "m_IgnoreMipmapLimit": False,
        "m_MipmapLimitGroupName": "",
        "m_StreamingMipmaps": False,
        "m_StreamingMipmapsPriority": 0,
        "m_ImageCount": 1,
        "m_TextureDimension": 2,
        "m_TextureSettings": {
            "m_FilterMode": 1,
            "m_Aniso": 1,
            "m_MipBias": 0.0,
            "m_WrapU": 1,
            "m_WrapV": 1,
            "m_WrapW": 1,
        },
        "m_LightmapFormat": 6,
        "m_ColorSpace": 1,
        "m_PlatformBlob": [],
        "image data": raw_pixels,
        "m_StreamData": {
            "offset": 0,
            "size": 0,
            "path": "",
        },
    }
    w = EndianBinaryWriter(endian="<")
    TypeTreeHelper.write_typetree(tex_dict, st.node, w)
    return w.bytes


def build_bundle():
    if not os.path.exists(RU_JSON_PATH):
        raise FileNotFoundError(f"Missing translation file: {RU_JSON_PATH}")

    png_files = sorted(f for f in os.listdir(TEXTURES_DIR) if f.lower().endswith(".png"))
    if not png_files:
        raise RuntimeError(f"No PNG files found in {TEXTURES_DIR}")

    # Borrow an empty/stripped SerializedFile shell matching Unity 2022.3.62f2 header layout
    env_template = UnityPy.load(GAME_RESOURCES_ASSETS)
    sf = list(env_template.files.values())[0]
    sf._enable_type_tree = True
    sf.externals = []
    sf.script_types = []
    sf.ref_types = []
    sf.flags = 4

    st_ab = make_serialized_type(142)
    st_ta = make_serialized_type(49)
    st_tx = make_serialized_type(28)
    sf.types = [st_ab, st_ta, st_tx]

    objects = {}
    preload_table = []
    container = []

    # path_id = 2 -> ru.json TextAsset ("ru")
    with open(RU_JSON_PATH, "r", encoding="utf-8") as f:
        ru_json_text = f.read()
    ta_bytes = serialize_text_asset(st_ta, "ru", ru_json_text)
    objects[2] = make_object(sf, 2, 1, 49, st_ta, ta_bytes)
    preload_table.append({"m_FileID": 0, "m_PathID": 2})
    container.append(
        (
            "assets/translations/ru.json",
            {"preloadIndex": 0, "preloadSize": 1, "asset": {"m_FileID": 0, "m_PathID": 2}},
        )
    )

    # path_id = 3..N -> Texture2D assets
    next_pid = 3
    for png_name in png_files:
        stem = os.path.splitext(png_name)[0]
        png_path = os.path.join(TEXTURES_DIR, png_name)
        tx_bytes = serialize_texture2d(st_tx, stem, png_path)
        objects[next_pid] = make_object(sf, next_pid, 2, 28, st_tx, tx_bytes)
        preload_idx = len(preload_table)
        preload_table.append({"m_FileID": 0, "m_PathID": next_pid})
        container.append(
            (
                f"assets/textures/{png_name.lower()}",
                {"preloadIndex": preload_idx, "preloadSize": 1, "asset": {"m_FileID": 0, "m_PathID": next_pid}},
            )
        )
        next_pid += 1

    ab_dict = {
        "m_Name": "rtlc.bundle",
        "m_PreloadTable": preload_table,
        "m_Container": container,
        "m_MainAsset": {"preloadIndex": 0, "preloadSize": 0, "asset": {"m_FileID": 0, "m_PathID": 0}},
        "m_RuntimeCompatibility": 1,
        "m_AssetBundleName": "rtlc.bundle",
        "m_Dependencies": [],
        "m_IsStreamedSceneAssetBundle": False,
        "m_ExplicitDataLayout": 1,
        "m_PathFlags": 7,
        "m_SceneHashes": [],
    }
    w_ab = EndianBinaryWriter(endian="<")
    TypeTreeHelper.write_typetree(ab_dict, st_ab.node, w_ab)
    objects[1] = make_object(sf, 1, 0, 142, st_ab, w_ab.bytes)

    sf.objects = objects

    bf = BundleFile.__new__(BundleFile)
    bf.signature = "UnityFS"
    bf.version = 7
    bf.version_player = "5.x.x"
    bf.version_engine = UNITY_VERSION
    bf.dataflags = ArchiveFlags(194)
    bf._block_info_flags = 2
    bf._uses_block_alignment = True
    bf.files = {"CAB-rtlc": sf}

    bundle_bytes = bf.save(packer="lz4")
    with open(OUT_BUNDLE_PATH, "wb") as f:
        f.write(bundle_bytes)

    print(f"Built {OUT_BUNDLE_PATH} ({len(bundle_bytes)} bytes, 1 TextAsset + {len(png_files)} Texture2D).")


def verify_bundle():
    env = UnityPy.load(OUT_BUNDLE_PATH)
    print(f"Verified container entries: {len(env.container)}")
    ta_count = 0
    tex_count = 0
    for obj in env.objects:
        if obj.type.name == "TextAsset":
            ta = obj.read()
            raw = ta.m_Script
            print(f"  TextAsset '{ta.m_Name}' ({len(raw)} chars)")
            ta_count += 1
        elif obj.type.name == "Texture2D":
            tex = obj.read()
            im = tex.image
            print(f"  Texture2D '{tex.m_Name}' {im.size} fmt={tex.m_TextureFormat}")
            tex_count += 1
    assert ta_count == 1, f"Expected 1 TextAsset, got {ta_count}"
    assert tex_count == 14, f"Expected 14 Texture2D, got {tex_count}"
    print("Bundle verification passed.")


def main():
    global GAME_RESOURCES_ASSETS
    parser = argparse.ArgumentParser(description="Build the localization Unity AssetBundle.")
    parser.add_argument(
        "--game-resources",
        default=GAME_RESOURCES_ASSETS,
        help="Path to the game's TheChildrenOfClay_Data/resources.assets file (or set THE_CHILDREN_OF_CLAY_RESOURCES_ASSETS).",
    )
    args = parser.parse_args()
    if not args.game_resources:
        parser.error("provide --game-resources or set THE_CHILDREN_OF_CLAY_RESOURCES_ASSETS")
    if not os.path.isfile(args.game_resources):
        parser.error(f"game resources file not found: {args.game_resources}")

    GAME_RESOURCES_ASSETS = os.path.abspath(args.game_resources)
    build_bundle()
    verify_bundle()


if __name__ == "__main__":
    main()
