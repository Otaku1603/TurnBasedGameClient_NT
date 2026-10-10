@echo off
rem ============================================================
rem 重新生成客户端协议代码
rem   源: 服务端仓库 src/main/proto/game.proto（协议 v2, csharp_namespace=GameClient.Proto）
rem   产物: Assets/Plugins/Proto/Gen/Game.cs（勿手改）
rem   依赖: 本机 protoc（D:\protoc\bin，libprotoc 33.x，配 Google.Protobuf 3.33.4 runtime）
rem 用法: tools\gen_proto.cmd
rem ============================================================
setlocal
set SERVER=d:\GitHome\TurnBasedGameServer_NT
set OUT=%~dp0..\Assets\Plugins\Proto\Gen

if not exist "%SERVER%\src\main\proto\game.proto" (
  echo [ERROR] game.proto not found: %SERVER%\src\main\proto\game.proto
  exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"

D:\protoc\bin\protoc.exe --csharp_out="%OUT%" --proto_path="%SERVER%\src\main\proto" "%SERVER%\src\main\proto\game.proto"
if errorlevel 1 (
  echo [ERROR] protoc failed
  exit /b 1
)
echo [OK] Generated %OUT%\Game.cs
endlocal
