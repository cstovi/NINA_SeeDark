param(
    [Parameter(Mandatory)][string]$Folder,
    [int]$GapMinutes = 5
)

$timestamp  = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$outputFile = Join-Path $PSScriptRoot "dark_temp_analysis_$timestamp.txt"
$script:log = [System.Text.StringBuilder]::new()

function Write-Log {
    param([string]$msg = "", [System.ConsoleColor]$ForegroundColor = [System.ConsoleColor]::Gray)
    Write-Host $msg -ForegroundColor $ForegroundColor
    $null = $script:log.AppendLine($msg)
}

function Get-FitsValue([string]$block, [string]$keyword) {
    $kw = $keyword.PadRight(8)
    for ($i = 0; $i + 80 -le $block.Length; $i += 80) {
        $card = $block.Substring($i, 80)
        if ($card.Substring(0, 8) -eq $kw -and $card[8] -eq '=') {
            $raw = $card.Substring(10).Trim()
            if ($raw.StartsWith("'")) { return $raw.Substring(1).Split("'")[0].Trim() }
            return ($raw -split '\s+')[0].Trim()
        }
    }
    return $null
}

$frames = @()
Get-ChildItem -Path $Folder -Recurse -Include "*.fit","*.fits" |
    Where-Object { $_.Name -notmatch '^master' } | ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    $block = [System.Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(5760, $bytes.Length))

    $imagetyp = Get-FitsValue $block 'IMAGETYP'
    if (-not $imagetyp -or $imagetyp -notmatch 'DARK') { return }

    $dateStr = Get-FitsValue $block 'DATE-OBS'
    if (-not $dateStr) { $dateStr = Get-FitsValue $block 'DATE-LOC' }
    $tempStr  = Get-FitsValue $block 'CCD-TEMP'
    $expStr   = Get-FitsValue $block 'EXPTIME'
    if (-not $expStr) { $expStr = Get-FitsValue $block 'EXPOSURE' }
    $ambStr   = Get-FitsValue $block 'AMBTEMP'

    if (-not $dateStr -or -not $tempStr) { return }

    try {
        $frames += [PSCustomObject]@{
            File     = $_.Name
            Time     = [datetime]::Parse($dateStr, [System.Globalization.CultureInfo]::InvariantCulture)
            Temp     = [double]$tempStr
            Exposure = if ($expStr) { [double]$expStr } else { 0 }
            Amb      = if ($ambStr) { [double]$ambStr } else { [double]::NaN }
        }
    } catch {}
}

if ($frames.Count -eq 0) { Write-Log "No DARK frames found in $Folder"; exit }

$frames = $frames | Sort-Object Time

# Split into sessions
$sessions = @()
$current  = @($frames[0])
for ($i = 1; $i -lt $frames.Count; $i++) {
    $gap = ($frames[$i].Time - $frames[$i-1].Time).TotalMinutes
    if ($gap -gt $GapMinutes) {
        $sessions += ,@($current)
        $current = @($frames[$i])
    } else {
        $current += $frames[$i]
    }
}
$sessions += ,@($current)

$riseRatesPerMin   = @()
$riseRatesPerFrame = @()
$coolingRates      = @()

Write-Log "`n===== Dark Temperature Analysis =====" -ForegroundColor Cyan
Write-Log "Folder  : $Folder"
Write-Log "Frames  : $($frames.Count)  |  Sessions: $($sessions.Count)  |  Gap threshold: ${GapMinutes}min`n"

for ($s = 0; $s -lt $sessions.Count; $s++) {
    $sess     = $sessions[$s]
    $start    = $sess[0]
    $end      = $sess[-1]
    $count    = $sess.Count
    $dTemp    = $end.Temp - $start.Temp
    $dMin     = ($end.Time - $start.Time).TotalMinutes
    $rateMin  = if ($dMin -gt 0) { [Math]::Round($dTemp / $dMin, 3) } else { 0 }
    $rateFrm  = if ($count -gt 1) { [Math]::Round($dTemp / ($count - 1), 3) } else { 0 }

    $ambDisplay = if ([double]::IsNaN($start.Amb)) { "amb: N/A" } else { "amb: {0:F1}degC" -f $start.Amb }
    Write-Log "--- Session $($s+1) ---" -ForegroundColor Yellow
    Write-Log ("  Start : {0:HH:mm:ss}  {1:F4}degC  ({2})" -f $start.Time, $start.Temp, $ambDisplay)
    Write-Log ("  End   : {0:HH:mm:ss}  {1:F4}degC" -f $end.Time,   $end.Temp)
    Write-Log ("  Frames: {0}  |  Exposure: {1}s" -f $count, $start.Exposure)
    Write-Log ("  Rise  : {0:+0.000;-0.000}degC  over {1:F1}min  →  {2:F3}degC/min  {3:F3}degC/frame" -f $dTemp, $dMin, $rateMin, $rateFrm)

    # Per-frame breakdown
    Write-Log "  Frames:"
    foreach ($f in $sess) {
        $elapsed = ($f.Time - $start.Time).TotalSeconds
        Write-Log ("    {0,6:F0}s  {1:F4}degC  ({2})" -f $elapsed, $f.Temp, $f.File)
    }

    if ($count -gt 1) {
        $riseRatesPerMin   += $rateMin
        $riseRatesPerFrame += $rateFrm
    }

    # Gap to next session
    if ($s -lt $sessions.Count - 1) {
        $nextStart   = $sessions[$s+1][0]
        $gapMin      = ($nextStart.Time - $end.Time).TotalMinutes
        $tempDrop    = $nextStart.Temp - $end.Temp
        $coolRateMin = if ($gapMin -gt 0) { [Math]::Round($tempDrop / $gapMin, 3) } else { 0 }
        Write-Log ("  Gap   : {0:F1}min idle  →  temp {1:+0.000;-0.000}degC  ({2:F3}degC/min)" -f $gapMin, $tempDrop, $coolRateMin) -ForegroundColor DarkCyan
        $coolingRates += $coolRateMin
    }
    Write-Log ""
}

# Summary
function Median([double[]]$arr) {
    $s = $arr | Sort-Object
    $mid = [int]($s.Count / 2)
    if ($s.Count % 2 -eq 1) { return $s[$mid] }
    return ($s[$mid-1] + $s[$mid]) / 2.0
}

Write-Log "===== Summary =====" -ForegroundColor Cyan
if ($riseRatesPerMin.Count -gt 0) {
    $meanRiseMin = [Math]::Round(($riseRatesPerMin | Measure-Object -Average).Average, 3)
    $medRiseMin  = [Math]::Round((Median $riseRatesPerMin), 3)
    $meanRiseFrm = [Math]::Round(($riseRatesPerFrame | Measure-Object -Average).Average, 3)
    $medRiseFrm  = [Math]::Round((Median $riseRatesPerFrame), 3)
    Write-Log ("  Rise rate  : mean {0:F3}degC/min  median {1:F3}degC/min" -f $meanRiseMin, $medRiseMin)
    Write-Log ("             : mean {0:F3}degC/frame  median {1:F3}degC/frame" -f $meanRiseFrm, $medRiseFrm)
}
if ($coolingRates.Count -gt 0) {
    $meanCool = [Math]::Round(($coolingRates | Measure-Object -Average).Average, 3)
    $medCool  = [Math]::Round((Median $coolingRates), 3)
    Write-Log ("  Cooling    : mean {0:F3}degC/min  median {1:F3}degC/min (during gaps)" -f $meanCool, $medCool)
}

# Suggest offset
$allSessions = $sessions | Where-Object { $_.Count -gt 1 }
if ($allSessions -and $riseRatesPerMin.Count -gt 0) {
    $medRateFrm = Median $riseRatesPerFrame
    $typFrames  = ($sessions | ForEach-Object { $_.Count } | Measure-Object -Average).Average
    $totalRise  = $medRateFrm * ($typFrames - 1)
    $offset     = [Math]::Round($totalRise / 2.0, 2)
    Write-Log ""
    Write-Log ("  Suggested bucket offset: -{0:F2}degC" -f $offset) -ForegroundColor Green
    $msg = "  (Start collecting when sensor is {0:F2}degC below bucket centre so the midpoint of the batch lands on the target temperature)" -f $offset
    Write-Log $msg
}
Write-Log ""

[System.IO.File]::WriteAllText($outputFile, $script:log.ToString(), [System.Text.Encoding]::UTF8)
Write-Host "Output saved to: $outputFile" -ForegroundColor DarkGray
