# Deixa no GitHub só as Releases que valem: as 2 versões normais mais recentes (a atual e a anterior, para dar para
# voltar rápido se a atual sair com problema) e a beta mais recente. As outras Releases são apagadas; as TAGS ficam
# sempre (dá para refazer uma Release rodando o workflow de novo para a tag dela).
#
# O atualizador do app (Velopack) só lê a Release mais recente de cada canal, e cada Release traz o pacote completo:
# as antigas não fazem falta para ninguém atualizar.
#
# Roda no fim do workflow "Publicar versão". Para ver o que seria apagado, sem apagar nada:
#   pwsh .github/limpar-releases.ps1 -Repositorio hyfronx/olho-no-chat -Simular
param(
    [Parameter(Mandatory)] [string] $Repositorio,
    [int] $QuantasNormais = 2,
    [int] $QuantasBetas = 1,
    [switch] $Simular
)
$ErrorActionPreference = 'Stop'

$todas = gh release list --repo $Repositorio --limit 500 --json tagName,isPrerelease,isDraft,createdAt | ConvertFrom-Json
# Rascunhos ficam de fora (ninguém atualiza por eles e alguém pode estar mexendo neles)
$publicadas = @($todas | Where-Object { -not $_.isDraft } | Sort-Object { [DateTimeOffset]$_.createdAt } -Descending)
$listaNormais = @($publicadas | Where-Object { -not $_.isPrerelease })
$listaBetas = @($publicadas | Where-Object { $_.isPrerelease })

$ficam = @($listaNormais | Select-Object -First $QuantasNormais) + @($listaBetas | Select-Object -First $QuantasBetas)
$apagar = @($listaNormais | Select-Object -Skip $QuantasNormais) + @($listaBetas | Select-Object -Skip $QuantasBetas)

Write-Host "Ficam: $(($ficam | ForEach-Object tagName) -join ', ')"
if ($apagar.Count -eq 0) {
    Write-Host 'Nada para apagar.'
    return
}
foreach ($release in $apagar) {
    if ($Simular) {
        Write-Host "Seria apagada: $($release.tagName)"
        continue
    }
    # Sem --cleanup-tag: a tag fica
    gh release delete $release.tagName --repo $Repositorio --yes
    Write-Host "Apagada: $($release.tagName)"
}
