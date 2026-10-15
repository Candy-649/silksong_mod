# Draws a red circle and dot on a spot of a MapDraw picture, a line to a label, and a title in the top left corner.
# Windows PowerShell 5.1 (System.Drawing):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\MapDraw\mark.ps1 -In area.png -Out marked.png `
#       -Sx <spot x> -Sy <spot y> -Lx <label x> -Ly <label y> -Label <text> -Title <text>
# The spot comes from <out prefix>_marks.json (sx, sy); put the label where the picture is empty.
param([string]$In, [string]$Out, [double]$Sx, [double]$Sy, [double]$Lx, [double]$Ly, [string]$Label, [string]$Title)
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile($In)
$bmp = New-Object System.Drawing.Bitmap $src
$src.Dispose()
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$red = [System.Drawing.Color]::FromArgb(255, 255, 72, 58)
$shade = [System.Drawing.Color]::FromArgb(230, 10, 8, 8)
$text = [System.Drawing.Color]::FromArgb(255, 240, 236, 228)

# Line from the circle to the label
$r = 40
$dx = $Lx - $Sx; $dy = $Ly - $Sy; $len = [Math]::Sqrt($dx * $dx + $dy * $dy)
$x0 = $Sx + $dx / $len * $r; $y0 = $Sy + $dy / $len * $r
$g.DrawLine((New-Object System.Drawing.Pen($shade, 8)), [float]$x0, [float]$y0, [float]$Lx, [float]$Ly)
$g.DrawLine((New-Object System.Drawing.Pen($red, 3.5)), [float]$x0, [float]$y0, [float]$Lx, [float]$Ly)

# Circle and dot on the spot
$g.DrawEllipse((New-Object System.Drawing.Pen($shade, 11)), [float]($Sx - $r), [float]($Sy - $r), [float](2 * $r), [float](2 * $r))
$g.DrawEllipse((New-Object System.Drawing.Pen($red, 5.5)), [float]($Sx - $r), [float]($Sy - $r), [float](2 * $r), [float](2 * $r))
$g.FillEllipse((New-Object System.Drawing.SolidBrush $shade), [float]($Sx - 9), [float]($Sy - 9), 18, 18)
$g.FillEllipse((New-Object System.Drawing.SolidBrush $red), [float]($Sx - 6.5), [float]($Sy - 6.5), 13, 13)

# Label in a box at the end of the line
$font = New-Object System.Drawing.Font('Microsoft YaHei', 30, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$size = $g.MeasureString($Label, $font)
$box = New-Object System.Drawing.RectangleF([float]($Lx + 6), [float]($Ly - $size.Height / 2 - 4), [float]($size.Width + 14), [float]($size.Height + 8))
$g.FillRectangle((New-Object System.Drawing.SolidBrush $shade), $box)
$g.DrawRectangle((New-Object System.Drawing.Pen($red, 2.5)), $box.X, $box.Y, $box.Width, $box.Height)
$g.DrawString($Label, $font, (New-Object System.Drawing.SolidBrush $red), [float]($box.X + 7), [float]($box.Y + 4))

$titleFont = New-Object System.Drawing.Font('Microsoft YaHei', 24, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString($Title, $titleFont, (New-Object System.Drawing.SolidBrush $text), 18, 12)

$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
