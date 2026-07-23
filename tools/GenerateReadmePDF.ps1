# GenerateReadmePDF.ps1
# Converts README.md to README.pdf using inline markdown->HTML conversion
# and Microsoft Edge headless printing.

[CmdletBinding()]
param(
    [string]$RepoPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    $RepoPath = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { Get-Location }
}

$repoPath   = (Resolve-Path $RepoPath).Path
$readmeMd   = Join-Path $repoPath "README.md"
$tempHtml   = Join-Path $env:TEMP ("README_temp_{0}.html" -f ([guid]::NewGuid().ToString("N")))
$outputPdf  = Join-Path $repoPath "README.pdf"

function ConvertFrom-ReadmeMarkdown($text) {
    $lines  = $text -split "`n"
    $html   = ""
    $inCode = $false
    $inTable = $false

    foreach ($line in $lines) {
        $line = $line.TrimEnd("`r")

        if ($line -match '^```') {
            if ($inCode) {
                $html += "</code></pre>`n"
                $inCode = $false
            } else {
                $html += "<pre><code>"
                $inCode = $true
            }
            continue
        }

        if ($inCode) {
            $escaped = $line -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;'
            $html += "$escaped`n"
            continue
        }

        if ($line -match '^\|') {
            if ($line -match '^\|[-| ]+\|$') { continue }
            if (-not $inTable) {
                $html += "<table>`n<thead>`n"
                $inTable = $true
                $isHeader = $true
            } else {
                $isHeader = $false
            }
            $cells = ($line -replace '^\|','') -replace '\|$','' -split '\|'
            $tag = if ($isHeader) { "th" } else { "td" }
            if ($isHeader) { $html += "</thead><tbody>`n" }
            $html += "<tr>"
            foreach ($cell in $cells) {
                $html += "<$tag>$(Convert-InlineMarkdown $cell.Trim())</$tag>"
            }
            $html += "</tr>`n"
            continue
        } elseif ($inTable) {
            $html += "</tbody></table>`n"
            $inTable = $false
        }

        if ($line -match '^### (.+)') {
            $html += "<h3>$(Convert-InlineMarkdown $matches[1])</h3>`n"; continue
        }
        if ($line -match '^## (.+)') {
            $html += "<h2>$(Convert-InlineMarkdown $matches[1])</h2>`n"; continue
        }
        if ($line -match '^# (.+)') {
            $html += "<h1>$(Convert-InlineMarkdown $matches[1])</h1>`n"; continue
        }
        if ($line -match '^---+$') {
            $html += "<hr>`n"; continue
        }
        if ($line -match '^> (.+)') {
            $html += "<blockquote>$(Convert-InlineMarkdown $matches[1])</blockquote>`n"; continue
        }
        if ($line -match '^- (.+)') {
            $html += "<ul><li>$(Convert-InlineMarkdown $matches[1])</li></ul>`n"; continue
        }
        if ($line -match '^\d+\. (.+)') {
            $html += "<ol><li>$(Convert-InlineMarkdown $matches[1])</li></ol>`n"; continue
        }
        if ($line.Trim() -eq '') {
            $html += "<p></p>`n"; continue
        }
        if ($line -match '^\s*!\[([^\]]*)\]\(([^)]+)\)\s*$') {
            $html += "<p><img src=""$($matches[2])"" alt=""$($matches[1])"" style=""max-width:100%;height:auto;""></p>`n"
            continue
        }

        $html += "<p>$(Convert-InlineMarkdown $line)</p>`n"
    }

    if ($inTable) { $html += "</tbody></table>`n" }
    if ($inCode)  { $html += "</code></pre>`n" }

    return $html
}

function Convert-InlineMarkdown($s) {
    $s = $s -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;'
    $s = $s -replace '\*\*(.+?)\*\*','<strong>$1</strong>'
    $s = $s -replace '\*(.+?)\*','<em>$1</em>'
    $s = $s -replace '`(.+?)`','<code>$1</code>'
    $s = $s -replace '!\[([^\]]*)\]\(([^)]+)\)','<img src="$2" alt="$1" style="max-width:100%;height:auto;">'
    $s = $s -replace '\[(.+?)\]\((.+?)\)','<a href="$2">$1</a>'
    return $s
}

if (-not (Test-Path $readmeMd)) {
    throw "README.md not found at $readmeMd"
}

Write-Host "Reading README.md..."
$md = Get-Content $readmeMd -Raw -Encoding UTF8

Write-Host "Converting to HTML..."
$body = ConvertFrom-ReadmeMarkdown $md

$fullHtml = @"
<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<title>Electronic Logbook - README</title>
<style>
  body { font-family: Segoe UI, Arial, sans-serif; max-width: 800px; margin: 40px auto; padding: 0 30px; color: #24292e; line-height: 1.6; font-size: 13px; }
  h1 { font-size: 2em; border-bottom: 2px solid #eaecef; padding-bottom: 0.3em; }
  h2 { font-size: 1.4em; border-bottom: 1px solid #eaecef; padding-bottom: 0.2em; margin-top: 28px; }
  h3 { font-size: 1.1em; margin-top: 20px; }
  code { background: #f6f8fa; padding: 2px 5px; border-radius: 3px; font-family: Consolas, monospace; font-size: 0.9em; }
  pre { background: #f6f8fa; padding: 14px; border-radius: 6px; overflow-x: auto; }
  pre code { background: none; padding: 0; }
  blockquote { border-left: 4px solid #dfe2e5; padding: 4px 16px; color: #6a737d; margin: 0 0 12px 0; }
  table { border-collapse: collapse; width: 100%; margin: 16px 0; }
  th { background: #f6f8fa; font-weight: 600; }
  th, td { border: 1px solid #dfe2e5; padding: 7px 12px; text-align: left; }
  tr:nth-child(even) { background: #f9f9f9; }
  hr { border: 0; border-top: 1px solid #eaecef; margin: 24px 0; }
  a { color: #0366d6; }
  ul, ol { padding-left: 24px; margin: 4px 0; }
  ul ul, ol ol, ul ol, ol ul { margin: 2px 0; }
  p { margin: 6px 0; }
  @media print { body { margin: 20px; } }
</style>
</head>
<body>
$body
</body>
</html>
"@

Write-Host "Embedding images..."
$imgMatches = [System.Text.RegularExpressions.Regex]::Matches($fullHtml, '<img src="([^"]+)"')
Write-Host "  Checking for img tags: $($imgMatches.Count) found"
foreach ($match in $imgMatches) {
    $imgRelPath = $match.Groups[1].Value
    $imgAbsPath = Join-Path $repoPath $imgRelPath
    if (Test-Path $imgAbsPath) {
        $bytes  = [System.IO.File]::ReadAllBytes($imgAbsPath)
        $b64    = [Convert]::ToBase64String($bytes)
        $ext    = [System.IO.Path]::GetExtension($imgAbsPath).TrimStart('.').ToLower()
        $mime   = if ($ext -eq 'jpg') { 'jpeg' } else { $ext }
        $newSrc = "<img src=""data:image/$mime;base64,$b64"""
        $fullHtml = $fullHtml.Replace($match.Value, $newSrc)
        Write-Host "  Embedded: $imgRelPath"
    } else {
        Write-Host "  WARNING: Image not found: $imgAbsPath" -ForegroundColor Yellow
    }
}

$fullHtml | Out-File -FilePath $tempHtml -Encoding UTF8
Write-Host "HTML written to temp file"

Write-Host "Converting to PDF via Microsoft Edge..."
$edgePaths = @(
    "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Microsoft\Edge\Application\msedge.exe"
)

$edgePath = $null
foreach ($path in $edgePaths) {
    if (Test-Path $path) { $edgePath = $path; break }
}

if (-not $edgePath) {
    throw "Microsoft Edge not found. HTML file saved at: $tempHtml"
}

$edgeArgs = "--headless --disable-gpu --print-to-pdf=`"$outputPdf`" --print-to-pdf-no-header --no-pdf-header-footer `"$tempHtml`""
$proc = Start-Process -FilePath $edgePath -ArgumentList $edgeArgs -Wait -PassThru -WindowStyle Hidden

if ($proc.ExitCode -ne 0 -or -not (Test-Path $outputPdf)) {
    throw "PDF generation failed (exit code $($proc.ExitCode)). HTML file is still available at: $tempHtml"
}

Remove-Item $tempHtml -ErrorAction SilentlyContinue
Write-Host "PDF saved to: $outputPdf" -ForegroundColor Green
