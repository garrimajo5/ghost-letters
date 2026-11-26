import csv
import json
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

    def test_weighted_review_covers_every_noncustom_card_including_inactive(self):
        # Server membership at the visual review, including manually moved and inactive cards.
        ranges = [(1,21),(34,89),(294,330),(332,343),(345,345),(456,456),(563,588),(739,758)]
        custom = {f'orig_{n:04}' for first,last in ranges for n in range(first,last+1)}
        rows = retag_cards.load('weights.json')
        labels = json.loads(retag_cards.LABELS_PATH.read_text(encoding='utf-8'))
        self.assertEqual(set(self.tags) - custom, set(rows))
        self.assertEqual({'black': .5, 'white': .5}, rows['orig_0666']['colors'])
        self.assertEqual({'green': 1}, rows['zhizha_0161']['colors'])
        for card, row in rows.items():
            for tag in [*row['meanings'], *row.get('details', {})]:
                self.assertRegex(labels.get(tag, ''), r'[А-Яа-яЁё]', (card, tag))
                self.assertEqual(check_tags.group(tag), 0, (card, tag))
            self.assertLessEqual(len(row['colors']), 3, card)
            self.assertAlmostEqual(sum(row['colors'].values()), 1, msg=card)
            self.assertEqual(list(row['meanings'].values()), sorted(row['meanings'].values(), reverse=True), card)
            self.assertEqual(next(iter(row['meanings'].values())), 1, card)

    def test_construction_platform_does_not_describe_a_baby_cradle(self):
        tags = set(self.tags['orig_0718'])
        self.assertTrue({'construction', 'lift', 'metal', 'work'} <= tags)
        self.assertFalse(tags & {'child', 'bedroom', 'lie-on', 'soft', 'wood'})
        self.assertNotIn('family', {a['tag'] for a in self.assoc['orig_0718']})

    def test_storage_and_fuel_tools_do_not_inherit_unrelated_profiles(self):
        for card in ('orig_0213', 'orig_0302', 'orig_0346'):
            self.assertIn('store', self.tags[card])
            self.assertNotIn('sound', self.tags[card])
            self.assertNotIn('play', self.tags[card])
        for card in ('orig_0242', 'orig_0442', 'orig_0624', 'orig_0689'):
            self.assertNotIn('electric', self.tags[card])
        for card in ('orig_0375', 'orig_0598', 'orig_0696'):
            self.assertNotIn('kitchen', self.tags[card])

    def test_literal_objects_are_not_confused_with_their_symbols(self):
        self.assertIn('jewelry', self.tags['orig_0463'])
        self.assertNotIn('animal', self.tags['orig_0463'])
        self.assertNotIn('insect', self.tags['orig_0076'])
        self.assertNotIn('shoot', self.tags['orig_0680'])
        self.assertIn('concert', self.tags['orig_0614'])
        self.assertNotIn('transport', self.tags['orig_0614'])

    def test_every_visual_review_row_reproduces_the_saved_tags(self):
        with Path(retag_cards.__file__).with_name('retag_review.csv').open(encoding='utf-8') as source:
            rows = list(csv.DictReader(source, delimiter=';'))
        original = {card for card in self.tags if card.startswith('orig_')}
        self.assertEqual(original, {r['card'] for r in rows})
        self.assertEqual(len(rows), len(original))
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
