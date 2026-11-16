# Sets IMAGE_FILE_LARGE_ADDRESS_AWARE in a 32-bit executable so it can use 4 GB on 64-bit Windows.
param([Parameter(Mandatory = $true)][string]$Path)

$bytes = [IO.File]::ReadAllBytes($Path)
$peHeader = [BitConverter]::ToInt32($bytes, 0x3C)
$characteristics = $peHeader + 4 + 18
$flags = [BitConverter]::ToUInt16($bytes, $characteristics)
if (($flags -band 0x20) -eq 0) {
    $flags = $flags -bor 0x20
    $bytes[$characteristics] = [byte]($flags -band 0xFF)
    $bytes[$characteristics + 1] = [byte]($flags -shr 8)
    [IO.File]::WriteAllBytes($Path, $bytes)
}
