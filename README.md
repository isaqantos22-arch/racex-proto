# RaceX Proto

Protótipo de corrida 3D em Unity 6 (URP) com servidor multiplayer em Node.js + Socket.io.

## Abrir e jogar
1. Unity Hub > Add > pasta `unity/` (Unity 6000.3.x).
2. Menu **RaceX > Setup Project** (cria cenas, URP e Build Settings; só é necessário uma vez).
3. Abra `Assets/Scenes/MainMenu.unity` e aperte Play.

## Controles
| Ação | Teclado (1 jogador) | Split-screen J1 | Split-screen J2 |
|---|---|---|---|
| Acelerar / Frear-ré | W/S ou setas | W/S | Setas |
| Direção | A/D ou setas | A/D | Setas |
| Freio de mão | Espaço | Espaço | Ctrl direito |
| Câmera | C | C | Shift direito |
| Pausa | Esc | Esc | Esc |

Na corrida também há os botões CAM e II (pausa) no canto superior direito de cada tela.

Gamepad e controles de toque (mobile) também são suportados.

## Testes
- Unity (PlayMode, modo batch): `./run_unity_tests.sh` (variável opcional `FILTER=RaceSmokeTests`).
- Servidor: `cd server && npm install && npm test`.

## Conteúdo
- Carros com modelos 3D importados (`Assets/Resources/CarModels/`): Falcon = Corvette C7 (OBJ), Titan = Porsche (Blender), Storm e F1 = F1 2022 (Blender). Pintura aplicada só nos materiais de carroceria.
- Pistas: Circuito Verde (floresta), Desfiladeiro Solar (deserto, ~2000 m) e Metrópole Noturna (cidade à noite, ~1700 m).
- Câmeras (tecla C): 3ª pessoa, 1ª pessoa, volante e frontal.
- Fumaça/poeira dos pneus com partículas cinza de borda suave.

## Builds
- Windows: `sh build_windows.sh` gera `build/Windows/RaceXProto.exe`.
- Android: `sh build_android.sh` gera `build/Android/RaceXProto.apk` (~84 MB). Ainda não testado em aparelho físico.

## Diagnóstico com capturas
`RaceXProto.exe -racexshots <pasta>` percorre menus e corridas (4 carros x 4 câmeras + teste de fumaça), salva PNGs e grava `autoshots.txt` com velocidade e confirmação de que cada carro anda para frente.

## Desvios da especificação
- Sem Cinemachine e sem pacote Splines: câmera e spline da pista implementadas em código.
- Pista e cenário feitos com primitivas; só os carros usam modelos 3D importados.
- Sem pódio 3D. Mini-mapa e áudio incluídos (motor sintetizado em código, sem assets).
- Desempenho dos três modelos equalizado por decisão de projeto: na especificação eram 210/260/310 km/h (Falcon/Titan/Storm); aqui ficam em 238/248/256 km/h, com aceleração quase igual.
- Modo online: o servidor está pronto e testado, mas o cliente Unity mostra "EM BREVE".
- Carro extra F1 (318 km/h, 8 marchas): opção "Categoria" GT/F1 no menu de corrida (F1 = corrida só de F1) e opção "F1 em corridas GT" nas configurações.

## Menu: o que funciona e o que é só visual
- Funcionam: JOGAR, CONFIGURAÇÕES, RECORDES, CRÉDITOS, SAIR, seleção de carro/cor do J1 e J2 lado a lado no split, Esc para voltar.
- Só visual por enquanto: ONLINE ("EM BREVE"), GARAGEM (vitrine com barras de atributos), PROGRESSO (barras em 0%), tela de carregamento (criada, ainda não ligada).
- No cockpit (câmeras Volante e 1ª pessoa) a alavanca de câmbio se move de forma diferente para R e para cada marcha.

## Status
- Fase 1 (single player, IA, menus, HUD, split-screen local): implementada e coberta pelos testes.
- Fases 2 a 5 (online no cliente, conteúdo extra, polimento, publicação): pendentes.
