param([ValidateSet('Desktop', 'Browser')][string]$Target = 'Desktop')

dotnet run --project "$PSScriptRoot/src/Murmuration.$Target"