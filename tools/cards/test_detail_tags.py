import json
import unittest
from pathlib import Path


class DetailTagsTest(unittest.TestCase):
    def test_catalog_has_valid_weighted_details_and_manual_marks(self):
        root = Path(__file__).resolve().parents[2] / 'client/assets/cards'
        tags = json.loads((root / 'tags.json').read_text(encoding='utf-8'))
        details = json.loads((root / 'details.json').read_text(encoding='utf-8'))
        manual = json.loads((root / 'details_manual.json').read_text(encoding='utf-8'))
        self.assertEqual(set(tags), set(details))
        for card, marks in details.items():
            self.assertEqual(len(marks), len({d['tag'] for d in marks}), card)
            for mark in marks:
                self.assertTrue(mark['tag'] and mark['label'], card)
                self.assertTrue(0 < mark['weight'] <= 1, card)
        for card, marks in manual.items():
            for mark in marks:
                self.assertIn(mark, details[card])


if __name__ == '__main__':
    unittest.main()
