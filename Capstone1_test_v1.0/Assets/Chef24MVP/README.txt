Chef24 MVP - 「등록금은 장사로 갚는다」 최소 버전 (기숙사 1일차, 타코야끼)
===============================================================================
목표: 문서 전체를 다 만들기 전에 "손님 주문 → 조리 → 판매 → 정산" 한 바퀴가 끝까지 돌아가는 판을 먼저 만든다.

위치
- Capstone1_test_v1.0 Unity 프로젝트 안에 들어 있음 (따로 복사할 필요 없음)
- 필요한 모델(푸드트럭 glb, 타코야끼 fbx)과 손님 VRM도 이미 Assets에 있음
- 손님 걷기 모션은 Mixamo FBX가 필요함 → 프로젝트 README.md의 "Mixamo 애니메이션" 참고. 없으면 손님이 미끄러지듯 움직임

씬 만들기
- Tools > Chef24 > Build MVP Scene → Assets/Chef24MVP/Chef24MVP.unity 생성 후 Play

플레이 방법
- WASD 이동, 마우스 시점, E 상호작용, R 주문 거절, Esc 일시정지
- 손님이 창구에 오면 손님을 보고 E (주문 받기) / R (거절)
- 그릴을 보고 E → 조리 바 2단계(반죽 붓기, 굽고 뒤집기). Space(또는 E)로 초록(노랑=완벽) 구간에 멈추기
- 다 익으면 그릴에서 E로 담기 (오래 두면 탐!)
- 소스 병 / 마요 병 / 파 그릇을 보고 E로 토핑 넣기. 손님이 빼 달라고 한 건 넣지 말 것
- 손님을 보고 E로 건네기. 주문과 맞으면 판매(굽기 등급 × 손님 유형별 가격), 틀리면 폐기 비용
- 잘못 만든 건 쓰레기통에서 E로 버리기
- 오전 8시 ~ 오후 7시(실제 5.5분) 영업 후 정산 → 목표 달성 / 미달성, 재도전

코드 구조 (Scripts)
- Core: GameManager(돈·흐름·정산), DayClock(시간·낮밤), GameInput(키 입력 한곳), Models(메뉴·주문·접시 데이터)
- Player: PlayerController(1인칭 이동), Interactor(E/R 상호작용), PlayerHands(들고 있는 음식)
- Cooking: GrillStation, TimingMiniGame(조리 바 판정), ToppingStation, TrashBin
- Customers: Customer(주문·인내심·판정), CustomerSpawner(등장, 점심시간 교직원 증가)
- UI: UIManager(HUD·말풍선·조리 바·시작/일시정지/결과 화면을 코드로 생성)
- Editor: Chef24MvpSceneBuilder(씬 자동 생성)

조정하기 쉬운 값 (씬의 Game 오브젝트 Inspector)
- GameManager: 목표 금액(targetAmount, 임시 60,000원), 폐기 비용, 메뉴 가격
- DayClock: secondsPerHour(게임 1시간 = 실제 몇 초)
- CustomerSpawner: 손님 유형별 인내심, 가격 배수, 등장 확률, 등장 간격

문서와 다른 점 / 아직 안 한 것
- 목표 금액: 문서는 스테이지당 300만원이지만 메뉴 가격과 맞지 않아 임시 값 사용 (밸런스 결정 필요)
- 로그인, SQLite 저장, 설정/키 변경, 스토리, 튜토리얼, 스테이지 지도, 나머지 메뉴 3종, 이벤트는 다음 단계
