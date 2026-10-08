import os
import sys
import tempfile
import unittest

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import visual_tags  # noqa: E402

TEAL = (40, 68, 79)


def card(draw) -> str:
    img = Image.new("RGB", (256, 256), TEAL)
    draw(ImageDraw.Draw(img))
    path = os.path.join(tempfile.mkdtemp(), "c.webp")
    img.save(path)
    return path


class VisualTagsTest(unittest.TestCase):
    def test_red_ball_is_red_and_round(self):
        tags = visual_tags.visual_tags(card(lambda d: d.ellipse((70, 70, 186, 186), fill=(200, 30, 30))))
        self.assertIn("red", tags)
        self.assertIn("shape-round", tags)

    def test_yellow_stick_is_long_and_wide(self):
        tags = visual_tags.visual_tags(card(lambda d: d.rectangle((30, 118, 226, 138), fill=(230, 200, 40))))
        self.assertIn("yellow", tags)
        self.assertIn("shape-long", tags)
        self.assertIn("shape-wide", tags)

    def test_star_is_spiky(self):
        star = [(128, 20), (150, 100), (236, 100), (166, 150), (196, 236), (128, 180), (60, 236), (90, 150), (20, 100), (106, 100)]
        tags = visual_tags.visual_tags(card(lambda d: d.polygon(star, fill=(240, 140, 30))))
        self.assertIn("shape-spiky", tags)

    def test_merge_replaces_old_shapes_keeps_meaning(self):
        merged = visual_tags.merge(["knife", "red", "shape-round"], ["red", "shape-long"])
        self.assertEqual(merged, ["knife", "red", "shape-long"])


if __name__ == "__main__":
    unittest.main()
