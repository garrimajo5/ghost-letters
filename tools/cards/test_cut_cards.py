import json
import os
import random
import sys
import tempfile
import unittest

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cut_cards  # noqa: E402

CELL = 120


def pattern(seed: int) -> Image.Image:
    """Уникальная «карта»: случайные фигуры на сером фоне."""
    rnd = random.Random(seed)
    img = Image.new("RGB", (CELL, CELL), (60, 70, 90))
    d = ImageDraw.Draw(img)
    for _ in range(12):
        x, y = rnd.randrange(CELL), rnd.randrange(CELL)
        r = rnd.randrange(8, 30)
        color = tuple(rnd.randrange(256) for _ in range(3))
        d.ellipse((x - r, y - r, x + r, y + r), fill=color)
    return img


class CutCardsTest(unittest.TestCase):
    def test_skips_empty_backs_and_duplicates(self):
        with tempfile.TemporaryDirectory() as res, tempfile.TemporaryDirectory() as out:
            back = pattern(999)
            back.save(os.path.join(res, "back.png"))

            # Лист 3x2: карты 1, 2, 3, рубашка, пусто, дубликат карты 1.
            sheet = Image.new("RGB", (CELL * 3, CELL * 2), (0, 0, 0))
            for i, img in enumerate([pattern(1), pattern(2), pattern(3), back, None, pattern(1)]):
                if img is not None:
                    sheet.paste(img, ((i % 3) * CELL, (i // 3) * CELL))
            sheet.save(os.path.join(res, "sheet.png"))

            catalog = {"back_refs": ["back.png"], "sheets": [{"file": "sheet.png", "cols": 3, "rows": 2}]}
            with open(os.path.join(res, "sheets.json"), "w", encoding="utf-8") as f:
                json.dump(catalog, f)

            code = cut_cards.main(["--resource", res, "--out", out,
                                   "--catalog", os.path.join(res, "sheets.json"), "--size", "64",
                                   "--sets", os.path.join(res, "no-sets.json")])
            self.assertEqual(code, 0)

            with open(os.path.join(out, "cards.json"), encoding="utf-8") as f:
                manifest = json.load(f)
            cards = manifest["sets"][0]["cards"]
            self.assertEqual(manifest["sets"][0]["code"], "original")
            self.assertEqual([c["id"] for c in cards], ["orig_0001", "orig_0002", "orig_0003"])
            for c in cards:
                with Image.open(os.path.join(out, c["file"])) as im:
                    self.assertEqual(im.size, (64, 64))

    def test_hamming(self):
        self.assertEqual(cut_cards.hamming(0b1010, 0b0110), 2)


if __name__ == "__main__":
    unittest.main()


class SplitBySetsTest(unittest.TestCase):
    def test_cards_go_to_their_sets_unknown_to_original(self):
        cards = [{"id": "orig_0001"}, {"id": "orig_0002"}, {"id": "orig_0003"}]
        manifest = cut_cards.split_by_sets(cards, {"orig_0001": "mirror", "orig_0002": "ritual"})
        sets = {s["code"]: [c["id"] for c in s["cards"]] for s in manifest["sets"]}
        self.assertEqual(sets, {"original": ["orig_0003"], "ritual": ["orig_0002"], "mirror": ["orig_0001"]})
        self.assertEqual([s["code"] for s in manifest["sets"]], ["original", "ritual", "mirror"])

    def test_real_mapping_covers_every_card_once(self):
        mapping = cut_cards.load_set_map()
        self.assertTrue(set(mapping.values()) <= set(cut_cards.SET_TITLES))
        self.assertGreater(len(mapping), 700)
