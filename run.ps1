param(
	[ValidateSet('Desktop', 'Browser')]
	[string]$Target = 'Desktop',
	[int]$Count
)

$appArgs = @()
if ($PSBoundParameters.ContainsKey('Count')) {
	$appArgs += "/count:$Count"
}

dotnet run --project "$PSScriptRoot/src/Murmuration.$Target" -- @appArgs