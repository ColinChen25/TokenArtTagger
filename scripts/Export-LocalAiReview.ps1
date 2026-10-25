param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$Instance,
    [switch]$FetchMissingPreviews
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiRuntime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiEvaluation.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalAiMcp.psm1') -Force
$repo=Split-Path $PSScriptRoot -Parent
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $OutputDirectory.StartsWith(([IO.Path]::GetFullPath((Join-Path $repo '.cache'))+'\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Review output must remain inside the ignored .cache directory.'}
$manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'manifest.json') | ConvertFrom-Json
$records=@{}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $OutputDirectory 'items') -Filter '*.json'){
    $r=Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName | ConvertFrom-Json
    if($r.fingerprint -eq $manifest.fingerprint){
        if($records.ContainsKey($r.path)){throw 'Multiple results for a path; resolve source changes before reviewing.'}
        $records[$r.path]=$r
    }
}
$previews=Join-Path $OutputDirectory 'previews'
New-Item -ItemType Directory -Force -Path $previews | Out-Null
$client=$null
try {
    if($FetchMissingPreviews){
        if(-not $Instance){throw 'Specify the app instance when fetching missing previews.'}
        $client=Start-LocalAiMcp $repo $Instance
        $status=(Invoke-LocalAiTool $client 'app_status').structuredContent
        $null=Invoke-LocalAiTool $client 'tag_schema'
        if($status.root -ne $manifest.root){throw 'App scan differs from the benchmark library.'}
        $catalog=@{};$offset=0
        do{
            $page=(Invoke-LocalAiTool $client 'list_images' @{limit=100;offset=$offset;expectedWorkspaceRevision=$status.workspaceRevision}).structuredContent
            foreach($i in $page.images){$catalog[$i.path]=$i};$offset=$page.nextOffset
        }while($null -ne $offset)
        foreach($entry in @($manifest.items)+@($manifest.references)){
            if($null -eq $entry){continue}
            $isReference=$null -ne $entry.tags
            $r=if($isReference){$entry}else{$records[$entry.path]};if(-not $r){continue}
            $target=Join-Path $previews ($r.sha256+'.png')
            if(Test-Path -LiteralPath $target){continue}
            $i=$catalog[$entry.path];if(-not $i){throw 'Sample no longer exists in the app scan.'}
            $detail=(Invoke-LocalAiTool $client 'image_details' @{id=$i.id;includeHash=$true}).structuredContent
            if($detail.sha256 -ne $r.sha256){throw 'Source changed since inference; do not display a different image beside this prediction.'}
            $dimension=if($isReference){$manifest.settings.referenceMaxDimension}else{$manifest.settings.maxDimension}
            $pixels=Invoke-LocalAiTool $client 'view_image' @{id=$i.id;expectedRevision=$detail.image.revision;maxDimension=$dimension}
            $picture=@($pixels.content | Where-Object type -eq 'image')
            if($picture.Count -ne 1){throw 'Preview response has no unique image.'}
            [IO.File]::WriteAllBytes($target,[Convert]::FromBase64String($picture[0].data))
        }
    }
} finally {if($client){Stop-LocalAiMcp $client}}

function ConvertTo-ReviewHtml($Value){[Net.WebUtility]::HtmlEncode([string]$Value)}
function TagText($Value){if($null -eq $Value){'unresolved / not applicable'}else{[string]$Value}}
$rows=@($manifest.items | ForEach-Object {if($records.ContainsKey($_.path)){$records[$_.path]}})
$focus=if($manifest.settings.focus){$manifest.settings.focus}else{'all'}
$summary=Get-AiEvaluationSummary $rows -Focus $focus
$annotations=@{}
$annotationPath=Join-Path $OutputDirectory 'content-annotations.json'
if(Test-Path -LiteralPath $annotationPath){
    $loadedAnnotations=Get-Content -Raw -Encoding UTF8 -LiteralPath $annotationPath | ConvertFrom-Json
    foreach($a in $loadedAnnotations){$annotations[$a.sha256]=$a}
}
$groups=[ordered]@{}
foreach($group in @('ordinary_clothed','minimal_outfit','artistic_nudity','succubus_like','unreviewed')){
    $members=@($rows | Where-Object {if($annotations.ContainsKey($_.sha256)){$group -in $annotations[$_.sha256].groups}else{$group -eq 'unreviewed'}})
    if($members.Count){$groups[$group]=Get-AiEvaluationSummary $members -Focus $focus}
}
Write-AiCheckpoint $groups (Join-Path $OutputDirectory 'content-group-summary.json')
$html=New-Object Text.StringBuilder
[void]$html.Append(@'
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Local AI tagging review</title><style>
body{font:16px/1.5 system-ui,sans-serif;margin:0;background:#f5f4ef;color:#202b35}main{max-width:1280px;margin:auto;padding:32px}h1{margin-bottom:4px}p{max-width:950px}.notice{padding:14px 18px;background:#fff1cf;border-left:4px solid #9e6b00}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(420px,1fr));gap:20px}article{background:white;border:1px solid #d8dde0;border-radius:10px;padding:18px;overflow-wrap:anywhere}img{width:100%;height:350px;object-fit:contain;background:#e8eaed;border-radius:6px}h2{font-size:17px}table{border-collapse:collapse;width:100%;font-size:14px}th,td{text-align:left;border-bottom:1px solid #e2e5e8;padding:7px}th{color:#475569}.different{background:#fff0ed}.badge{display:inline-block;padding:2px 8px;border-radius:4px;background:#e9eef3;font-size:13px}.muted{color:#5b6570;font-size:14px}details{margin-top:12px}pre{white-space:pre-wrap;font-size:12px;max-height:360px;overflow:auto}@media(max-width:520px){main{padding:14px}.cards{grid-template-columns:1fr}}@media print{article{break-inside:avoid}.cards{display:block}article{margin:12px 0}details{display:none}}
</style><main><h1>Local AI tagging review</h1>
<p class="notice">All outputs are suggestions. No tags or filenames were changed. Existing filename labels remain authoritative. This is a deliberately difficult diagnostic sample, not an estimate of whole-library accuracy.</p>
'@)
$matchLabel=if($focus -eq 'all'){'full tag matches'}else{'matches for '+($summary.evaluatedFields -join ' + ')}
[void]$html.Append('<p>'+ (ConvertTo-ReviewHtml ($summary.attempted.ToString()+' / '+@($manifest.items).Count+' images processed; '+$summary.validOutputs+' schema-valid outputs; '+$summary.exactFocusedMatches+' '+$matchLabel+' among '+$summary.labeled+' labeled attempts.'))+'</p>')
if($focus -ne 'all'){[void]$html.Append('<p class="notice">This experiment evaluates only '+(ConvertTo-ReviewHtml ($summary.evaluatedFields -join ', '))+'. Other fields are deliberately blank; no full-tag accuracy is claimed.</p>')}
[void]$html.Append('<p class="muted">CPU only &middot; two threads &middot; one target at a time &middot; '+(ConvertTo-ReviewHtml $manifest.settings.model)+' &middot; prompt '+(ConvertTo-ReviewHtml $manifest.settings.promptVersion)+'</p>')
if(@($manifest.references).Count -and $null -ne $manifest.references){
    [void]$html.Append('<h2>Trusted visual references</h2><p>These fixed examples were supplied for every target below. They are excluded from accuracy scoring.</p><div class="cards">')
    foreach($reference in $manifest.references){
        [void]$html.Append('<article><h2>'+(ConvertTo-ReviewHtml ([IO.Path]::GetFileName($reference.path)))+'</h2>')
        if(Test-Path -LiteralPath (Join-Path $previews ($reference.sha256+'.png'))){[void]$html.Append('<img loading="lazy" src="previews/'+$reference.sha256+'.png" alt="Trusted reference artwork">')}
        $referenceTagText=(@('gender','role','weaponOrStyle','race') | ForEach-Object {TagText $reference.tags.$_}) -join ' / '
        [void]$html.Append('<p>'+(ConvertTo-ReviewHtml $referenceTagText)+'</p></article>')
    }
    [void]$html.Append('</div><h2>Target predictions</h2>')
}
[void]$html.Append('<div class="cards">')
foreach($entry in $manifest.items){
    $r=$records[$entry.path]
    [void]$html.Append('<article><h2>'+(ConvertTo-ReviewHtml ([IO.Path]::GetFileName($entry.path)))+'</h2>')
    if(-not $r){[void]$html.Append('<p>Pending</p></article>');continue}
    $preview=Join-Path $previews ($r.sha256+'.png')
    if(Test-Path -LiteralPath $preview){[void]$html.Append('<img loading="lazy" src="previews/'+$r.sha256+'.png" alt="Character artwork used for this prediction">')}
    [void]$html.Append('<p><span class="badge">'+(ConvertTo-ReviewHtml $r.outcome)+'</span> <span class="muted">'+(ConvertTo-ReviewHtml ('{0:N1} seconds' -f $r.elapsedSeconds))+'</span></p>')
    [void]$html.Append('<table><tr><th>Field</th><th>Filename label</th><th>AI suggestion</th></tr>')
    foreach($field in $summary.evaluatedFields){
        $expected=if($r.hasGroundTruth){TagText $r.expected.$field}else{'No trusted label'}
        $prediction=if($r.prediction){TagText $r.prediction.tags.$field}else{'No accepted prediction'}
        $class=if($r.hasGroundTruth -and ($r.outcome -notin @('complete','partial') -or $r.expected.$field -cne $r.prediction.tags.$field)){' class="different"'}else{''}
        [void]$html.Append('<tr'+$class+'><td>'+(ConvertTo-ReviewHtml $field)+'</td><td>'+(ConvertTo-ReviewHtml $expected)+'</td><td>'+(ConvertTo-ReviewHtml $prediction)+'</td></tr>')
    }
    [void]$html.Append('</table>')
    if($r.prediction){[void]$html.Append('<p>'+(ConvertTo-ReviewHtml $r.prediction.evidence)+'</p><p class="muted">'+(ConvertTo-ReviewHtml $r.prediction.uncertainty)+'</p>')}
    if($r.reason){[void]$html.Append('<p>'+(ConvertTo-ReviewHtml $r.reason)+'</p>')}
    if($annotations.ContainsKey($r.sha256)){[void]$html.Append('<p class="muted">Content review: '+(ConvertTo-ReviewHtml ($annotations[$r.sha256].groups -join ', '))+'. '+(ConvertTo-ReviewHtml $annotations[$r.sha256].note)+'</p>')}
    [void]$html.Append('<details><summary>Raw model output</summary><pre>'+(ConvertTo-ReviewHtml $r.response.message.content)+'</pre></details></article>')
}
[void]$html.Append('</div></main></html>')
$reportPath=Join-Path $OutputDirectory 'review.html'
[IO.File]::WriteAllText($reportPath,$html.ToString(),(New-Object Text.UTF8Encoding($false)))
Write-Output "Review: $reportPath"
