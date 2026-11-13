import csv
import unittest
from pathlib import Path

import check_tags
import retag_cards


class ReviewedTagsTest(unittest.TestCase):
    def setUp(self):
        self.root = retag_cards.ROOT
        self.tags, self.details, self.manual, self.assoc = (
            retag_cards.load(f) for f in ('tags.json', 'details.json', 'details_manual.json', 'associations.json'))

    def test_catalog_is_complete_and_valid(self):
        self.assertEqual([], check_tags.validate(self.root, self.tags, self.details, self.manual, self.assoc))

    def test_every_visual_review_row_reproduces_the_saved_tags(self):
        with Path(retag_cards.__file__).with_name('retag_review.csv').open(encoding='utf-8') as source:
            rows = list(csv.DictReader(source, delimiter=';'))
        self.assertEqual(set(self.tags), {r['card'] for r in rows})
        self.assertEqual(len(rows), len(self.tags))
        for row in rows:
            self.assertEqual(self.tags[row['card']], retag_cards.reviewed_tags(row), row['card'])

    def test_missing_card_is_reported_without_key_error(self):
        del self.tags['orig_0027']
        errors = check_tags.validate(self.root, self.tags, self.details, self.manual, self.assoc)
        self.assertTrue(any('key coverage' in e for e in errors))

    def test_unknown_tag_and_unreliable_shape_are_rejected(self):
        self.tags['orig_0001'] += ['not-in-vocabulary', 'shape-diagonal']
        errors = check_tags.validate(self.root, self.tags, self.details, self.manual, self.assoc)
        self.assertTrue(any('unknown tag not-in-vocabulary' in e for e in errors))
        self.assertTrue(any('unreliable shape' in e for e in errors))

    def test_duplicate_main_association_and_missing_manual_detail_are_rejected(self):
        self.assoc['orig_0027'].append(dict(tag='liquid', weight=.9, label='жидкость'))
        self.details['orig_0027'] = []
        errors = check_tags.validate(self.root, self.tags, self.details, self.manual, self.assoc)
        self.assertTrue(any('duplicate, nonsemantic' in e for e in errors))
        self.assertTrue(any('manual detail not merged' in e for e in errors))

    def test_loading_order_and_secondary_weight_match_server_contract(self):
        result = check_tags.features({'a': ['glass', 'flower']},
                                    {'a': [dict(tag='flower', weight=.3, label='цветок')]},
                                    {'a': [dict(tag='glass', weight=.5, label='стекло'),
                                           dict(tag='memory', weight=.5, label='память')]})
        self.assertNotIn('flower', result['a'])
        self.assertEqual(1, result['a']['glass'])
        self.assertAlmostEqual(.0875, result['a']['memory'])

    def test_unmarked_color_does_not_create_a_connection(self):
        self.assertEqual(0, check_tags.similarity({'red': 1}, {'red': 1}))
        self.assertAlmostEqual(.05, check_tags.similarity({'dominant-red': 1}, {'dominant-red': 1}))


if __name__ == '__main__':
    unittest.main()
