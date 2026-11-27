# ANSI colors shared by build/verify output; compatible with PowerShell 5.1.
# Append CLRF before every newline so banners cannot color the following line.
$ansi_esc = "$([char]27)"
$CLRF = "$ansi_esc[0m"
$FG_GRAY = "$ansi_esc[37m"
$FG_LIGHTYELLOW = "$ansi_esc[93m"
$BANNER_BLUE_WHITE = "$ansi_esc[44;97m"
$BANNER_MAGENTA_WHITE = "$ansi_esc[45;97m"
$BANNER_GREEN_WHITE = "$ansi_esc[42;97m"
$BANNER_RED_WHITE = "$ansi_esc[41;97m"
