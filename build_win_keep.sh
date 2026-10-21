#!/bin/sh
cd 'C:/Users/Usuário/.verdent/verdent-projects/FINAL RACE'
UNITY="C:/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe"
rm -f unity_build.log
"$UNITY" -batchmode -nographics \
  -projectPath 'C:/Users/Usuário/.verdent/verdent-projects/FINAL RACE/unity' \
  -executeMethod RaceX.EditorTools.RaceXSetup.BuildWindowsFromCommandLine \
  -quit -logFile 'C:/Users/Usuário/.verdent/verdent-projects/FINAL RACE/unity_build.log' > /dev/null 2>&1
echo "EXIT=$?"
grep -aE "RACEX_BUILD|error CS" unity_build.log | head -5
