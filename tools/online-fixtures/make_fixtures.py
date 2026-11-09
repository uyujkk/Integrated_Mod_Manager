"""Synthetic online samples for IMM_ONLINE_FIXTURES (screenshots and UI tests only).

Everything here is made up: names, authors, numbers, and placeholder images drawn by this script.
"""
import io, json, os, struct, sys, zlib, colorsys

out = sys.argv[1]
os.makedirs(os.path.join(out, "img"), exist_ok=True)

def png(path, width, height, hue, stripes):
    rows = []
    for y in range(height):
        row = bytearray([0])
        for x in range(width):
            band = (x * stripes // width) % 2
            light = 0.42 + 0.18 * (y / height) + (0.08 if band else 0)
            r, g, b = colorsys.hls_to_rgb(hue, light, 0.45)
            row += bytes((int(r * 255), int(g * 255), int(b * 255)))
        rows.append(bytes(row))
    raw = b"".join(rows)
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)

CATEGORY = 90001
characters = [(90011, "Sample Hero Aster"), (90012, "Sample Hero Briar"), (90013, "Sample Hero Cobalt")]
IMG = "https://images.example.invalid/samples"
mods = []
for i in range(12):
    cat_id, cat_name = characters[i % 3]
    item = 990100 + i
    mods.append({
        "_idRow": item,
        "_sName": f"Sample Mod {i + 1:02d}",
        "_sProfileUrl": f"https://gamebanana.com/mods/{item}",
        "_aSubmitter": {"_sName": f"Sample Author {i % 4 + 1}", "_idRow": str(880000 + i % 4)},
        "_aRootCategory": {"_sName": "Skins"},
        "_aSubCategory": {"_sName": cat_name, "_sProfileUrl": f"https://gamebanana.com/mods/cats/{cat_id}/"},
        "_aPreviewMedia": {"_aImages": [{"_sBaseUrl": IMG, "_sFile530": f"mod-{i + 1:02d}.png", "_sFile": f"mod-{i + 1:02d}.png"}]},
        "_tsDateModified": 1767225600 - i * 86400, "_tsDateUpdated": 1767225600 - i * 86400, "_tsDateAdded": 1764633600 - i * 86400,
        "_nLikeCount": 40 - i * 3, "_nViewCount": 5000 - i * 300, "_nDownloadCount": 900 - i * 60,
    })
    png(os.path.join(out, "img", f"mod-{i + 1:02d}.png"), 530, 298, (i * 0.083) % 1.0, 6 + i % 4)

def page(records):
    return {"_aMetadata": {"_nRecordCount": len(records), "_nPerpage": 40, "_bIsComplete": True}, "_aRecords": records}

responses = []
def write(name, payload):
    with io.open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)

write("index-90001.json", page(mods))
responses.append({"prefix": f"https://gamebanana.com/apiv11/Mod/Index?_aFilters%5BGeneric_Category%5D={CATEGORY}&", "file": "index-90001.json"})
for cat_id, cat_name in characters:
    write(f"index-{cat_id}.json", page([m for m in mods if m["_aSubCategory"]["_sName"] == cat_name]))
    responses.append({"prefix": f"https://gamebanana.com/apiv11/Mod/Index?_aFilters%5BGeneric_Category%5D={cat_id}&", "file": f"index-{cat_id}.json"})
write("categories-90001.json", [{"_idRow": cat_id, "_sName": cat_name, "_nItemCount": 4, "_nCategoryCount": 0,
                                 "_sIconUrl": f"{IMG}/hero-{cat_id}.png"} for cat_id, cat_name in characters])
responses.append({"prefix": f"https://gamebanana.com/apiv11/Mod/Categories?_idCategoryRow={CATEGORY}&", "file": "categories-90001.json"})
for cat_id, _ in characters:
    png(os.path.join(out, "img", f"hero-{cat_id}.png"), 96, 96, ((cat_id - 90011) * 0.31 + 0.55) % 1.0, 2)
responses.append({"prefix": f"{IMG}/", "file": "img/mod-01.png"})
for i in range(12):
    responses.append({"prefix": f"{IMG}/mod-{i + 1:02d}.png", "file": f"img/mod-{i + 1:02d}.png"})
for cat_id, _ in characters:
    responses.append({"prefix": f"{IMG}/hero-{cat_id}.png", "file": f"img/hero-{cat_id}.png"})

# Details and card data of each Mod (api.gamebanana.com Core/Item/Data, json_min with keys).
for i, mod in enumerate(mods):
    item = mod["_idRow"]
    shots = [{"_sFile800": f"{IMG}/mod-{(i + k) % 12 + 1:02d}.png"} for k in range(3)]
    write(f"details-{item}.json", {
        "name": mod["_sName"],
        "description": f"A made-up sample Mod for screenshots and UI tests ({mod['_sName']}).",
        "text": "Synthetic description text. Line one of the sample.<br>Line two: nothing here comes from a real site.",
        "screenshots": json.dumps(shots),
        "Preview().sSubFeedImageUrl()": f"{IMG}/mod-{i + 1:02d}.png"})
    responses.append({"prefix": f"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={item}&fields=name%2Cdescription",
                      "file": f"details-{item}.json"})
    write(f"card-{item}.json", {
        "name": mod["_sName"], "Category().name": mod["_aSubCategory"]["_sName"],
        "catid": int(mod["_aSubCategory"]["_sProfileUrl"].rstrip("/").rsplit("/", 1)[1]),
        "RootCategory().name": "Skins", "likes": mod["_nLikeCount"], "views": mod["_nViewCount"], "downloads": mod["_nDownloadCount"],
        "userid": int(mod["_aSubmitter"]["_idRow"]), "Owner().name": mod["_aSubmitter"]["_sName"], "mdate": mod["_tsDateUpdated"],
        "Preview().sSubFeedImageUrl()": f"{IMG}/mod-{i + 1:02d}.png", "Url().sProfileUrl()": mod["_sProfileUrl"],
        "Url().sDownloadUrl()": f"https://files.example.invalid/sample-{item}.zip",
        "Files().aFiles()": {str(770000 + i): {"_idRow": str(770000 + i), "_sFile": f"sample-mod-{i + 1:02d}.zip",
                                                "_nFilesize": 2048, "_tsDateAdded": mod["_tsDateUpdated"], "_nDownloadCount": mod["_nDownloadCount"],
                                                "_sDownloadUrl": f"https://files.example.invalid/sample-{item}.zip", "_sVersion": "1.0",
                                                "_sDescription": "Sample archive", "_bIsArchived": False}},
        "Updates().bSubmissionHasUpdates()": False})
    responses.append({"prefix": f"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={item}&fields=name%2CCategory",
                      "file": f"card-{item}.json"})

# Translation of descriptions (shown in Chinese and Russian): one made-up sentence per language.
for lang, text in (("zh-CN", "（合成样例的说明译文，只用于截图和界面测试。）"), ("ru", "(Перевод синтетического описания, только для снимков и тестов.)"),
                   ("en", "(Translated synthetic description, for screenshots and tests only.)")):
    write(f"translate-{lang}.json", [[[text, "sample", None, None, 10]], None, "en"])
    responses.append({"prefix": f"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={lang}&", "file": f"translate-{lang}.json"})

write("fixtures.json", {"about": "Synthetic samples for IMM_ONLINE_FIXTURES (screenshots and UI tests only); made up, no real site data.",
                        "responses": responses})
print(len(responses), "responses")
