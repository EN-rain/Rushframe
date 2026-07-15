[CmdletBinding()]
param(
    [switch]$KeepArchives,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ArchiveRoot = Join-Path $ScriptRoot 'packages'
$ExtractRoot = Join-Path $ScriptRoot 'library'
$ManifestPath = Join-Path $ScriptRoot 'download-manifest.json'
$MaximumPackBytes = 300MB
$MaximumTotalBytes = 900MB

$Packs = @(
    [pscustomobject]@{
        Id = 'kenney-interface-sounds'
        Name = 'Kenney Interface Sounds'
        FileName = 'kenney_interface-sounds.zip'
        SourcePage = 'https://kenney.nl/assets/interface-sounds'
        DownloadUrl = 'https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip'
        License = 'Creative Commons CC0 1.0'
        LicenseUrl = 'https://creativecommons.org/publicdomain/zero/1.0/'
    },
    [pscustomobject]@{
        Id = 'kenney-digital-audio'
        Name = 'Kenney Digital Audio'
        FileName = 'kenney_digital-audio.zip'
        SourcePage = 'https://kenney.nl/assets/digital-audio'
        DownloadUrl = 'https://kenney.nl/media/pages/assets/digital-audio/216eac4753-1677590265/kenney_digital-audio.zip'
        License = 'Creative Commons CC0 1.0'
        LicenseUrl = 'https://creativecommons.org/publicdomain/zero/1.0/'
    },
    [pscustomobject]@{
        Id = 'kenney-rpg-audio'
        Name = 'Kenney RPG Audio'
        FileName = 'kenney_rpg-audio.zip'
        SourcePage = 'https://kenney.nl/assets/rpg-audio'
        DownloadUrl = 'https://kenney.nl/media/pages/assets/rpg-audio/8e99002d76-1677590336/kenney_rpg-audio.zip'
        License = 'Creative Commons CC0 1.0'
        LicenseUrl = 'https://creativecommons.org/publicdomain/zero/1.0/'
    },
    [pscustomobject]@{
        Id = 'kenney-impact-sounds'
        Name = 'Kenney Impact Sounds'
        FileName = 'kenney_impact-sounds.zip'
        SourcePage = 'https://kenney.nl/assets/impact-sounds'
        DownloadUrl = 'https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip'
        License = 'Creative Commons CC0 1.0'
        LicenseUrl = 'https://creativecommons.org/publicdomain/zero/1.0/'
    }
)

function Assert-ZipFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Archive was not created: $Path"
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $signature = New-Object byte[] 4
        if ($stream.Read($signature, 0, 4) -ne 4) {
            throw "Downloaded archive is too small: $Path"
        }
        $valid = $signature[0] -eq 0x50 -and $signature[1] -eq 0x4B -and (
            ($signature[2] -eq 0x03 -and $signature[3] -eq 0x04) -or
            ($signature[2] -eq 0x05 -and $signature[3] -eq 0x06) -or
            ($signature[2] -eq 0x07 -and $signature[3] -eq 0x08)
        )
        if (-not $valid) {
            throw "The server response is not a ZIP archive: $Path"
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-RemoteLength {
    param([Parameter(Mandatory)][string]$Url)

    try {
        $response = Invoke-WebRequest -Uri $Url -Method Head -MaximumRedirection 5 -UseBasicParsing
        $header = $response.Headers['Content-Length']
        if ($header) {
            $parsedLength = 0L
            if ([long]::TryParse([string]$header, [ref]$parsedLength)) {
                return $parsedLength
            }
        }
    }
    catch {
        Write-Verbose "HEAD request unavailable for ${Url}: $($_.Exception.Message)"
    }
    return 0L
}

New-Item -ItemType Directory -Path $ArchiveRoot -Force | Out-Null
New-Item -ItemType Directory -Path $ExtractRoot -Force | Out-Null

$totalExpected = 0L
foreach ($pack in $Packs) {
    $length = Get-RemoteLength -Url $pack.DownloadUrl
    if ($length -gt $MaximumPackBytes) {
        throw "$($pack.Name) is larger than the configured 300 MB safety limit."
    }
    $totalExpected += $length
}
if ($totalExpected -gt $MaximumTotalBytes) {
    throw 'The selected packs exceed the configured 900 MB total safety limit.'
}

$manifest = [System.Collections.Generic.List[object]]::new()
foreach ($pack in $Packs) {
    $archivePath = Join-Path $ArchiveRoot $pack.FileName
    $temporaryPath = "$archivePath.partial-$([guid]::NewGuid().ToString('N'))"
    $destination = Join-Path $ExtractRoot $pack.Id

    try {
        if ((Test-Path -LiteralPath $destination) -and -not $Force) {
            Write-Host "Skipping $($pack.Name): already extracted. Use -Force to replace it."
        }
        else {
            Write-Host "Downloading $($pack.Name)..."
            Invoke-WebRequest -Uri $pack.DownloadUrl -OutFile $temporaryPath -MaximumRedirection 5 -UseBasicParsing
            Assert-ZipFile -Path $temporaryPath

            if (Test-Path -LiteralPath $archivePath) {
                Remove-Item -LiteralPath $archivePath -Force
            }
            Move-Item -LiteralPath $temporaryPath -Destination $archivePath

            $staging = "$destination.extracting-$([guid]::NewGuid().ToString('N'))"
            New-Item -ItemType Directory -Path $staging -Force | Out-Null
            try {
                Expand-Archive -LiteralPath $archivePath -DestinationPath $staging -Force
                if (Test-Path -LiteralPath $destination) {
                    Remove-Item -LiteralPath $destination -Recurse -Force
                }
                Move-Item -LiteralPath $staging -Destination $destination
            }
            finally {
                if (Test-Path -LiteralPath $staging) {
                    Remove-Item -LiteralPath $staging -Recurse -Force
                }
            }
        }

        $hash = if (Test-Path -LiteralPath $archivePath) {
            (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        } else { $null }

        $audioCount = if (Test-Path -LiteralPath $destination) {
            @(Get-ChildItem -LiteralPath $destination -Recurse -File | Where-Object {
                $_.Extension -in '.wav', '.ogg', '.mp3', '.flac', '.m4a', '.aac'
            }).Count
        } else { 0 }

        $manifest.Add([pscustomobject]@{
            id = $pack.Id
            name = $pack.Name
            sourcePage = $pack.SourcePage
            downloadUrl = $pack.DownloadUrl
            license = $pack.License
            licenseUrl = $pack.LicenseUrl
            archiveSha256 = $hash
            extractedPath = $destination
            audioFileCount = $audioCount
            downloadedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        })

        if (-not $KeepArchives -and (Test-Path -LiteralPath $archivePath)) {
            Remove-Item -LiteralPath $archivePath -Force
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
Write-Host "Completed. Audio packs are under: $ExtractRoot"
Write-Host "Download and licence manifest: $ManifestPath"
