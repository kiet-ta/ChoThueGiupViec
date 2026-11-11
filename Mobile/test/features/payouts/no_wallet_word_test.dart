import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// LEDGER-24: the platform keeps a ledger of virtual amounts and must not look like an e-wallet, so no text of the
/// payouts feature (which the worker reads) says "ví". "Ví dụ" (for example) is a different word and stays allowed.
void main() {
  test('no text of the payouts feature uses the word "ví"', () {
    final word = RegExp(r'(^|[^\p{L}])[Vv]í([^\p{L}]|$)', unicode: true);
    final example = RegExp(r'[Vv]í dụ', unicode: true);
    final offenders = <String>[];

    final files = Directory('lib/features/payouts')
        .listSync(recursive: true)
        .whereType<File>()
        .where((f) => f.path.endsWith('.dart'));
    for (final file in files) {
      final lines = file.readAsLinesSync();
      for (var i = 0; i < lines.length; i++) {
        final line = lines[i];
        if (line.trimLeft().startsWith('//')) continue;
        final text = line.replaceAll(example, '');
        if (word.hasMatch(text)) offenders.add('${file.path}:${i + 1}: $line');
      }
    }

    expect(offenders, isEmpty);
  });
}
