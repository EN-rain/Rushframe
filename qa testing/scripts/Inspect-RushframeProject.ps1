param([Parameter(Mandatory=$true)][string]$Path)

$ErrorActionPreference = "Stop"
$project = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
Write-Output "PROJECT_NAME=$($project.name)"
Write-Output "MEDIA_COUNT=$($project.mediaLibrary.Count)"
Write-Output "TRACK_COUNT=$($project.mainSequence.tracks.Count)"
Write-Output "SEQUENCE_DURATION=$($project.mainSequence.duration.numerator / $project.mainSequence.duration.denominator)"
for ($trackIndex = 0; $trackIndex -lt $project.mainSequence.tracks.Count; $trackIndex++) {
    $track = $project.mainSequence.tracks[$trackIndex]
    Write-Output "TRACK|INDEX=$trackIndex|NAME=$($track.name)|KIND=$($track.kind)|ITEMS=$($track.items.Count)|MUTED=$($track.muted)|SOLO=$($track.solo)|LOCKED=$($track.locked)|HIDDEN=$($track.hidden)"
    for ($itemIndex = 0; $itemIndex -lt $track.items.Count; $itemIndex++) {
        $item = $track.items[$itemIndex]
        $color = $item.colorCorrection
        Write-Output "ITEM|TRACK=$trackIndex|INDEX=$itemIndex|KIND=$($item.kind)|START=$($item.timelineStart.numerator / $item.timelineStart.denominator)|DURATION=$($item.duration.numerator / $item.duration.denominator)|SOURCE_START=$($item.sourceStart.numerator / $item.sourceStart.denominator)|SOURCE_DURATION=$($item.sourceDuration.numerator / $item.sourceDuration.denominator)|SPEED=$($item.speed)|REVERSED=$($item.reversed)|VOLUME=$($item.volume)|OPACITY=$($item.opacity)|POS=$($item.transform.positionX),$($item.transform.positionY)|SCALE=$($item.transform.scaleX),$($item.transform.scaleY)|ROTATION=$($item.transform.rotationDegrees)|BRIGHTNESS=$($color.brightness)|CONTRAST=$($color.contrast)|SATURATION=$($color.saturation)|EFFECTS=$($item.effects.Count)"
    }
}
for ($mediaIndex = 0; $mediaIndex -lt $project.mediaLibrary.Count; $mediaIndex++) {
    $media = $project.mediaLibrary[$mediaIndex]
    Write-Output "MEDIA|INDEX=$mediaIndex|KIND=$($media.kind)|DURATION=$($media.duration.numerator / $media.duration.denominator)|OFFLINE=$($media.isOffline)|PATH=$($media.originalPath)"
}
Write-Output "MARKERS=$($project.mainSequence.markers.Count)"
Write-Output "TRANSITIONS=$($project.mainSequence.transitions.Count)"
Write-Output "TASKS=$($project.tasks.Count)"
for ($taskIndex = 0; $taskIndex -lt $project.tasks.Count; $taskIndex++) {
    $task = $project.tasks[$taskIndex]
    Write-Output "TASK|INDEX=$taskIndex|TITLE=$($task.title)|COMPLETED=$($task.isCompleted)"
}
Write-Output "CAMPAIGN_DESCRIPTION=$($project.campaignDescription)"
