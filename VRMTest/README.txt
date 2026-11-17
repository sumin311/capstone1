VRM 테스트 씬 사용법
====================
1. Unity에 UniVRM을 먼저 설치하세요.
2. 이 VRMTest 폴더를 통째로 Unity 프로젝트의 Assets 폴더 안에 넣으세요.
3. test1_restored.vrm 도 Assets 폴더에 넣으세요.
4. 상단 메뉴 Tools > VRM Test > Create Test Scene 을 누르세요.
   (Project 창에서 .vrm 파일을 먼저 선택해두면 그 아바타로 만들어집니다.)
5. Assets/VRMTest/VRMTestScene 이 열리면 Play!

조작
- WASD / 방향키: 이동, Shift: 달리기, Space: 점프
- 마우스 오른쪽 드래그: 카메라 회전, 휠: 줌
- 1 기쁨 2 화남 3 슬픔 4 편안 5 놀람 6 눈감기 7 입(아) 0 초기화
- H: 도움말 숨기기/보이기

참고
- 애니메이션이 아직 없어서 T포즈로 미끄러지듯 움직이는 게 정상입니다.
- 걷는 동안 머리카락/옷이 흔들리면 스프링본이 정상 작동하는 것입니다.
- Mixamo 애니메이션을 넣을 때: Animator Controller에 Float 파라미터 "Speed"를 만들고
  Idle/Walk/Run을 Speed 값으로 전환하게 하면 이동 스크립트가 자동으로 값을 넣어줍니다.
