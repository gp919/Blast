#Requires -Version 5.1
<#
.SYNOPSIS
    어셈블리 참조 방향과 Simulation 계층 결정성 규칙을 검증합니다.

.DESCRIPTION
    docs/project_context.md 3번의 두 가지 규칙을 기계적으로 확인합니다.

    검사 1 (정확): asmdef 참조 그래프가 허용된 단방향 그래프와 일치하는가.
                   asmdef JSON을 직접 파싱하므로 오탐이 없습니다.
    검사 2 (휴리스틱): Simulation 어셈블리에 금지 심볼 참조가 0건인가.
                       한 줄 주석은 제거하고 검사하지만, 블록 주석과 문자열
                       리터럴 안의 단어는 걸릴 수 있습니다. 결과는 사람이 판단하세요.

.EXAMPLE
    pwsh tools/check-layering.ps1
    powershell -ExecutionPolicy Bypass -File tools/check-layering.ps1

.NOTES
    위반이 있으면 종료 코드 1을 반환합니다.
#>

[CmdletBinding()]
param(
    [string]$ScriptsRoot
)

$ErrorActionPreference = "Stop"

# param 기본값에서는 $PSScriptRoot 가 비어 있는 경우가 있어 본문에서 해석합니다.
if ([string]::IsNullOrEmpty($ScriptsRoot)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ScriptsRoot = Join-Path $scriptDirectory "..\Assets\Scripts"
}

$violations = New-Object System.Collections.Generic.List[string]

# 허용된 어셈블리 참조. 여기에 없는 Blast.* 참조는 전부 위반입니다.
$allowedReferences = @{
    "Blast.Core"         = @()
    "Blast.Simulation"   = @("Blast.Core")
    "Blast.Input"        = @("Blast.Core")
    "Blast.Presentation" = @("Blast.Core", "Blast.Simulation")
    "Blast.Network"      = @("Blast.Core", "Blast.Simulation")
}

# Simulation 계층 금지 심볼. 키는 정규식, 값은 위반 사유입니다.
$bannedInSimulation = [ordered]@{
    '\bTime\.'        = "Time API. 틱 인덱스와 고정 dt만 사용할 것"
    '\bRandom\.'      = "UnityEngine.Random. 시드 기반 자체 난수만 사용할 것"
    '\bTransform\b'   = "Transform 참조. 위치는 시뮬레이션 상태로 들고 Presentation이 반영할 것"
    '\bAnimator\b'    = "Animator 참조. Presentation 계층 소관"
    '\bRigidbody2D\b' = "Rigidbody2D 참조. BoxCast 커스텀 컨트롤러를 쓸 것"
}

if (-not (Test-Path $ScriptsRoot)) {
    Write-Host "Assets/Scripts 를 찾을 수 없습니다: $ScriptsRoot" -ForegroundColor Red
    exit 1
}

$ScriptsRoot = (Resolve-Path $ScriptsRoot).Path

# ---------------------------------------------------------------
# 검사 1. asmdef 참조 방향
# ---------------------------------------------------------------
Write-Host "[1/2] 어셈블리 참조 방향 검사" -ForegroundColor Cyan

$asmdefFiles = Get-ChildItem -Path $ScriptsRoot -Filter *.asmdef -Recurse -File
if ($asmdefFiles.Count -eq 0) {
    $violations.Add("asmdef 파일이 하나도 없습니다. 어셈블리 분리가 되어 있지 않습니다.")
}

foreach ($file in $asmdefFiles) {
    $asmdef = Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $name = $asmdef.name

    if (-not $allowedReferences.Contains($name)) {
        $violations.Add("알 수 없는 어셈블리 '$name' ($($file.Name)). 허용 목록에 추가하거나 이름을 확인하세요.")
        continue
    }

    $allowed = $allowedReferences[$name]
    foreach ($reference in $asmdef.references) {
        # GUID 형태 참조는 이름을 알 수 없으므로 건너뜁니다.
        if ($reference -like "GUID:*") {
            $violations.Add("$name -> '$reference' 가 GUID 참조입니다. 이름 참조로 바꿔야 이 검사가 동작합니다.")
            continue
        }
        # 외부 패키지 참조는 검사 대상이 아닙니다.
        if ($reference -notlike "Blast.*") { continue }

        if ($allowed -notcontains $reference) {
            $violations.Add("역방향 또는 미허용 참조: $name -> $reference")
        }
    }
    Write-Host "  $name -> $($asmdef.references -join ', ')"
}

# ---------------------------------------------------------------
# 검사 2. Simulation 계층 금지 심볼
# ---------------------------------------------------------------
Write-Host ""
Write-Host "[2/2] Simulation 계층 결정성 규칙 검사" -ForegroundColor Cyan

$simulationRoot = Join-Path $ScriptsRoot "Simulation"
$simulationFiles = @()
if (Test-Path $simulationRoot) {
    $simulationFiles = @(Get-ChildItem -Path $simulationRoot -Filter *.cs -Recurse -File)
}

if ($simulationFiles.Count -eq 0) {
    Write-Host "  Simulation 에 .cs 파일이 없습니다. 검사할 대상 없음."
}

foreach ($file in $simulationFiles) {
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # 한 줄 주석 제거. 블록 주석과 문자열 리터럴은 처리하지 않습니다.
        $code = $lines[$i] -replace '//.*$', ''
        if ([string]::IsNullOrWhiteSpace($code)) { continue }

        foreach ($pattern in $bannedInSimulation.Keys) {
            if ($code -cmatch $pattern) {
                $relative = $file.FullName.Substring($ScriptsRoot.Length + 1)
                $violations.Add("$relative`:$($i + 1)  $($bannedInSimulation[$pattern])")
            }
        }
    }
}

if ($simulationFiles.Count -gt 0) {
    Write-Host "  .cs 파일 $($simulationFiles.Count) 개 검사함"
}

# ---------------------------------------------------------------
# 결과
# ---------------------------------------------------------------
Write-Host ""
if ($violations.Count -eq 0) {
    Write-Host "통과. 위반 0건" -ForegroundColor Green
    Write-Host "참고: Dictionary 순회 순서 의존은 정적 검사로 잡히지 않습니다. 리뷰로 확인하세요." -ForegroundColor DarkGray
    exit 0
}

Write-Host "위반 $($violations.Count) 건" -ForegroundColor Red
foreach ($v in $violations) {
    Write-Host "  $v" -ForegroundColor Red
}
exit 1
