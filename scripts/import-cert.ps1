# 一键导入自签证书（用于安装未签名/自签 MSIX）
# 以管理员身份运行 PowerShell： .\scripts\import-cert.ps1
$cer = Join-Path $PSScriptRoot "YUNO-BASE.cer"
if (-not (Test-Path $cer)) { Write-Host "未找到 $cer"; exit 1 }

Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Write-Host "证书已导入受信任的发布者/根证书，可安装 MSIX。"
Write-Host "如仍失败：设置 → 隐私和安全性 → 开发者选项 → 打开开发人员模式"
