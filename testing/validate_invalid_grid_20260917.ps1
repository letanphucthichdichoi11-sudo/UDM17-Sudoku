$ErrorActionPreference = 'Stop'

$serverExe = (Resolve-Path 'UDM17-Sudoku/Sudoku.Server/bin/Debug/Sudoku.Server.exe').Path
$sharedDll = (Resolve-Path 'UDM17-Sudoku/Sudoku.Server/bin/Debug/Sudoku.Shared.dll').Path
$sharedAssembly = [Reflection.Assembly]::LoadFrom($sharedDll)
$assembly = [Reflection.Assembly]::LoadFrom($serverExe)
$managerType = $assembly.GetType('Sudoku.Server.Game.MatchManager', $true)
$durationType = $sharedAssembly.GetType('Sudoku.Shared.Models.MatchDurationMinutes', $true)
$method = @($managerType.GetMethods([Reflection.BindingFlags]'Public,Instance') | Where-Object { $_.Name -eq 'StartMatch' -and $_.GetParameters().Count -eq 7 -and $_.GetParameters()[6].ParameterType -eq $durationType })[0]
if (-not $method) { throw 'StartMatch seven-argument overload not found' }
$duration = [Enum]::Parse($durationType, 'Five')

function New-SolvedGrid {
    $grid = [int[,]]::new(9, 9)
    for ($row = 0; $row -lt 9; $row++) {
        for ($column = 0; $column -lt 9; $column++) {
            $grid[$row, $column] = (($row * 3 + [math]::Floor($row / 3) + $column) % 9) + 1
        }
    }
    return ,$grid
}

function New-Puzzle($solution) {
    $puzzle = [int[,]]$solution.Clone()
    $puzzle[0, 0] = 0
    return ,$puzzle
}

$cases = @(
    'puzzle-8x9',
    'solution-9x8',
    'puzzle-minus-one',
    'puzzle-ten',
    'solution-zero',
    'solution-ten',
    'clue-mismatch',
    'solution-duplicate',
    'puzzle-full'
)

"RUN TC-021 invalid-grid at=$(Get-Date -Format o)"
foreach ($case in $cases) {
    $solution = New-SolvedGrid
    $puzzle = New-Puzzle $solution
    switch ($case) {
        'puzzle-8x9' { $puzzle = [int[,]]::new(8, 9) }
        'solution-9x8' { $solution = [int[,]]::new(9, 8) }
        'puzzle-minus-one' { $puzzle[0, 1] = -1 }
        'puzzle-ten' { $puzzle[0, 1] = 10 }
        'solution-zero' { $solution[0, 1] = 0 }
        'solution-ten' { $solution[0, 1] = 10 }
        'clue-mismatch' { $puzzle[0, 1] = if ($solution[0, 1] -eq 9) { 1 } else { $solution[0, 1] + 1 } }
        'solution-duplicate' { $solution[0, 0] = $solution[0, 1]; $puzzle[0, 0] = 0 }
        'puzzle-full' { $puzzle = [int[,]]$solution.Clone() }
    }
    $manager = [Activator]::CreateInstance($managerType, $true)
    $field = $managerType.GetField('_matches', [Reflection.BindingFlags]'Instance,NonPublic')
    $before = $field.GetValue($manager).Count
    $errorType = $null
    $errorMessage = $null
    try {
        $null = $method.Invoke($manager, @([guid]::NewGuid(), [guid]::NewGuid(), 'grid-A', 'grid-B', $puzzle, $solution, $duration))
    }
    catch {
        $rootError = $_.Exception
        while ($rootError.InnerException) { $rootError = $rootError.InnerException }
        $errorType = $rootError.GetType().Name
        $errorMessage = $rootError.Message
    }
    $after = $field.GetValue($manager).Count
    [pscustomobject]@{ TestCase = 'TC-021'; Variant = $case; ErrorType = $errorType; ErrorMessage = $errorMessage; MatchCountBefore = $before; MatchCountAfter = $after } | ConvertTo-Json -Compress
}
"END TC-021 at=$(Get-Date -Format o)"
