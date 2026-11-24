[CmdletBinding()]
param(
    [string]$WixBin,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$project = (Get-Item -LiteralPath $PSScriptRoot).Parent.FullName
if (-not $WixBin) {
    $WixBin = if ($env:WIX) { Join-Path $env:WIX 'bin' } else { Split-Path (Get-Command candle.exe).Source }
}

function Invoke-WixTool([string]$Name, [string[]]$Arguments) {
    & (Join-Path $WixBin "$Name.exe") @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name 退出码为 $LASTEXITCODE" }
}

# WinUI 的两个多语言 DLL 超过 MSI 语言字段上限，部分独立目录的 MUI 语言编号不被 ICE 接受。
# MSI 按应用支持的中英文登记 DLL，按语言无关文件安装这三组目录内的资源，保留文件原样。
function Set-FileLanguages([string]$Msi, [xml]$Harvest) {
    $engine = New-Object -ComObject WindowsInstaller.Installer
    $database = $engine.OpenDatabase($Msi, 1)
    $update = $database.OpenView('UPDATE File SET Language = ? WHERE File = ?')
    try {
        foreach ($file in $Harvest.GetElementsByTagName('File')) {
            $relative = $file.Source.Substring('$(var.PublishDir)\'.Length)
            if ($relative -in 'Microsoft.ui.xaml.dll', 'Microsoft.UI.Xaml.Phone.dll') {
                $language = '1033,2052'
            } elseif ($relative -match '^(gd-GB|mi-NZ|ug-CN)\\Microsoft\.UI\.Xaml(\.Phone)?\.dll\.mui$') {
                $language = '0'
            } else {
                continue
            }
            $row = $engine.CreateRecord(2)
            $row.StringData(1) = $language
            $row.StringData(2) = $file.Id
            $update.Execute($row)
            $update.Close()
        }
        $database.Commit()
    } finally {
        $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($update)
        $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    }
}

[xml]$manifest = Get-Content -LiteralPath (Join-Path $project 'Package.appxmanifest') -Raw
$version = ([version]$manifest.Package.Identity.Version).ToString(3)
$scratch = [IO.Directory]::CreateDirectory((Join-Path $project 'obj/installer')).FullName
$work = [IO.Directory]::CreateDirectory((Join-Path $scratch ([guid]::NewGuid().ToString('N')))).FullName
$payload = Join-Path $work 'publish'
$publishArgs = @('publish', (Join-Path $project 'InkAudit.csproj'), '-c', 'Release', '-p:Platform=x64',
    '-p:PublishProfile=win-x64', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $payload)
if ($NoRestore) { $publishArgs += '--no-restore' }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 退出码为 $LASTEXITCODE" }
$exe = Get-Item -LiteralPath (Join-Path $payload 'InkAudit.exe')
if ([version]$exe.VersionInfo.FileVersion -ne [version]$manifest.Package.Identity.Version) {
    throw '应用版本与安装包版本不一致。'
}

$licensePath = Join-Path $project 'LICENSE'
Copy-Item -LiteralPath $licensePath -Destination (Join-Path $payload 'LICENSE.txt')
$license = [IO.File]::ReadAllText($licensePath).Replace('\', '\\').Replace('{', '\{').Replace('}', '\}')
$license = [regex]::Replace($license, '[^\x00-\x7f]', {
    param($match)
    $unit = [int][char]$match.Value
    if ($unit -gt 32767) { $unit -= 65536 }
    '\u' + $unit + '?'
})
$license = [regex]::Replace($license, '\r?\n', '\par ' + [Environment]::NewLine)
$rtf = Join-Path $work 'License.rtf'
[IO.File]::WriteAllText($rtf, '{\rtf1\ansi\uc1{\fonttbl{\f0 Segoe UI;}}\f0\fs18 ' + $license + '}', [Text.Encoding]::ASCII)

$filesPath = Join-Path $work 'Files.wxs'
Invoke-WixTool heat @('dir', $payload, '-nologo', '-ag', '-srd', '-sfrag', '-sreg', '-scom',
    '-dr', 'INSTALLFOLDER', '-cg', 'PublishedFiles', '-var', 'var.PublishDir', '-out', $filesPath)
# 主程序及其两个快捷方式由 Product.wxs 声明，移除 heat 自动生成的重复组件和引用。
[xml]$files = Get-Content -LiteralPath $filesPath -Raw
$launcher = ($files.GetElementsByTagName('File') | Where-Object Source -eq '$(var.PublishDir)\InkAudit.exe').ParentNode
$reference = $files.GetElementsByTagName('ComponentRef') | Where-Object Id -eq $launcher.Id
$null = $reference.ParentNode.RemoveChild($reference)
$null = $launcher.ParentNode.RemoveChild($launcher)
$files.Save($filesPath)

$conflict = [ComponentModel.Win32Exception]::new(1638).Message
Invoke-WixTool candle @('-nologo', '-wx', '-arch', 'x64', "-dPublishDir=$payload", "-dProductVersion=$version",
    "-dUpgradeCode=$($manifest.Package.Identity.Name)", "-dLicenseRtf=$rtf", "-dVersionConflictMessage=$conflict",
    '-out', ($work + '\'), (Join-Path $PSScriptRoot 'Product.wxs'), $filesPath)
$msi = Join-Path $work "InkAudit-$version-x64.msi"
# 先写入最终的文件语言信息，再对交付的 MSI 运行全套 ICE 校验。
Invoke-WixTool light @('-nologo', '-wx', '-cultures:zh-cn', '-ext', (Join-Path $WixBin 'WixUIExtension.dll'),
    '-sval', '-spdb', '-out', $msi, (Join-Path $work 'Product.wixobj'), (Join-Path $work 'Files.wixobj'))
Set-FileLanguages $msi $files
Invoke-WixTool smoke @('-nologo', '-wx', $msi)

$zip = Join-Path $work 'InkAudit-win-x64-portable.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip)
$output = [IO.Directory]::CreateDirectory((Join-Path $project "bin/installer/v$version")).FullName
Copy-Item -LiteralPath $msi, $zip -Destination $output -Force
Get-FileHash -LiteralPath (Join-Path $output ([IO.Path]::GetFileName($msi))), (Join-Path $output ([IO.Path]::GetFileName($zip)))
$generated = Get-Item -LiteralPath $work
if ($generated.Parent.FullName -eq $scratch -and -not $generated.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
    Remove-Item -LiteralPath $generated.FullName -Recurse -Force
}
