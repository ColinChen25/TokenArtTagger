"""Private static review of new-image classifier suggestions, without an apply action."""
import argparse
import html
import os

from encoder_common import checkpoint, file_hash, private_path, read_json


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--suggestions', required=True)
    parser.add_argument('--features', required=True)
    parser.add_argument('--inputs', required=True)
    args = parser.parse_args()
    output, features, inputs = map(private_path, (args.suggestions, args.features, args.inputs))
    status = read_json(output/'status.json')
    saved = read_json(output/'suggestions.json')
    settings = read_json(output/'settings.json')
    feature_settings = read_json(features/'settings.json')
    previews = read_json(inputs/'inputs.json')
    if (status['status'] != 'complete' or not status['cleanupComplete']
            or saved['fingerprint'] != settings['fingerprint'] or saved['fingerprint'] != status['fingerprint']
            or settings['settings']['targetFeatureFingerprint'] != feature_settings['fingerprint']
            or feature_settings['settings']['inputFingerprint'] != previews['fingerprint']
            or saved['automaticAcceptance'] or saved['scoresCalibrated'] or saved['finalTestEvaluated']):
        raise ValueError('Suggestion/preview provenance is inconsistent.')
    by_hash = {row['sha256']: row for row in previews['items']}
    if (len(by_hash) != len(previews['items']) or len(saved['items']) != len(by_hash)
            or {row['sha256'] for row in saved['items']} != set(by_hash)):
        raise ValueError('Preview and suggestion target sets differ.')
    cards = []
    for row in saved['items']:
        if not row['reviewRequired'] or row['automaticAcceptance'] or row['scoresCalibrated']:
            raise ValueError('Expected review-only suggestions.')
        sha = row['sha256']
        preview = by_hash[sha]
        record = read_json(features/'items'/f'{sha}.json')
        path = private_path(inputs/preview['preview'])
        if (record['sha256'] != sha or record['fingerprint'] != feature_settings['fingerprint']
                or not path.is_relative_to(inputs) or file_hash(path) != preview['previewSha256']
                or record['previewSha256'] != preview['previewSha256']
                or path.read_bytes()[:8] != b'\x89PNG\r\n\x1a\n'):
            raise ValueError('Suggestion image differs from verified feature input.')
        src = html.escape(os.path.relpath(path, output).replace('\\', '/'), quote=True)
        fields = ''.join('<tr><th>'+field+'</th><td>'+html.escape(str(row['prediction'][field] or 'Unresolved / not applicable'))+'</td></tr>'
                         for field in ('gender', 'role', 'weaponOrStyle', 'race'))
        scores = html.escape(', '.join(f'{name}: {value:.3f}' for name, value in row['scores'].items()))
        cards.append('<article><img loading="lazy" src="'+src+'" alt="Artwork to review"><h2>'+html.escape(sha[:12])+
                     '</h2><p class="notice">Needs manual review</p><table>'+fields+
                     '</table><details><summary>Uncalibrated classifier scores</summary><p>'+scores+
                     '</p><p>These values are not measured probabilities of correct tags.</p></details></article>')
    page='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Local classifier suggestions</title><style>body{font:16px/1.5 system-ui,sans-serif;background:#f5f4ef;color:#202b35;margin:0}main{max-width:1100px;margin:auto;padding:28px}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:20px}article{background:white;border:1px solid #d8dde0;border-radius:10px;padding:18px}img{width:100%;height:350px;object-fit:contain;background:#e8eaed}h2{font-size:17px}.notice{background:#fff1cf;padding:10px}table{border-collapse:collapse;width:100%}th,td{text-align:left;border-bottom:1px solid #e2e5e8;padding:7px}details{font-size:14px;margin-top:16px}</style><main><h1>Local classifier suggestions</h1>
<p class="notice">Every suggestion requires review. No app tags or filenames have changed. These images have no authoritative labels, so this report does not score accuracy.</p>
<p>The classifier learned from filename labels but cannot identify categories absent from its training data. A high score does not establish reliability.</p><div class="cards">'''+''.join(cards)+'</div></main></html>'
    (output/'review.html').write_text(page, encoding='utf-8')
    checkpoint(output/'review-validation.json', dict(cards=len(cards), verifiedPngReferences=len(cards),
        remoteResources=False, scripts=False, browserRendered=False, automaticAcceptance=False))
    print(f'Review: {output / "review.html"}')


if __name__ == '__main__':
    main()
