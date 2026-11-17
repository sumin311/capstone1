# 푸드트럭 내부 3D 모델 (기본 틀)

- 작성일: 2026-11-17
- 용도: 캡스톤1 3D 게임의 푸드트럭 내부 블록아웃 (참고 게임: Steam 과일버스)
- 제작: Claude가 손그림 단면도를 보고 파이썬 코드로 생성 (Blender 스크립트 + GLB)
- 상태: 기본 틀만 있음. 냄비/프라이팬/병/과일 등 소품은 팀에서 따로 모델링 예정

## 파일

| 파일 | 설명 |
|---|---|
| `foodtruck_basic.glb` | 완성 모델 (0.49MB, 메쉬 16개, 재질 15개, 냉장고 문 애니메이션 2개 포함) |
| `foodtruck_basic_blender.py` | Blender Scripting 탭에서 실행하면 같은 모델을 생성하는 스크립트 (외부 라이브러리 불필요) |

## 크기와 좌표

- 실내: 길이 6.4m (X) × 폭 3.6m (Y) × 높이 2.7m (Z), 벽 두께 0.1m. 단위 1 = 1m
- 좌표계: Blender 기준 X=오른쪽, Y=안쪽, Z=위. GLB는 glTF 규격(Y-up)으로 변환되어 있음
- Y=0이 앞면(판매 창구, 어닝 쪽), Y=3.6이 뒷벽
- 지붕은 단면으로 보기 위해 기본 제외 (스크립트에서 `SHOW_ROOF = True`로 켤 수 있음, GLB에는 없음)

## 구성 (오브젝트 16개)

| 오브젝트 | 내용 |
|---|---|
| Floor | 바닥판 + 체크 타일 |
| Wall_Back / Wall_Left / Wall_Right / Wall_Front | 벽 4면. 안쪽은 크림색, 바깥 3cm는 차체색(민트). 앞벽은 판매 창구 구멍 포함 |
| Service_Window | 창구 프레임, 창틀, 바깥 선반 |
| Awning | 빨강/흰 줄무늬 어닝 |
| Stove_Cabinet | 뒷벽 왼쪽 화구 캐비닛 (상판은 평평함. 버너 없음) |
| Hood | 화구 위 후드 + 덕트 |
| Prep_Cabinet | 화구 오른쪽 조리대 캐비닛 |
| Fridge | 오른쪽 뒤 모서리 냉장고 본체 (싱크대는 삭제됨) |
| Fridge_Door_Top / Fridge_Door_Bottom | 냉장고 문 (손잡이 포함, 열림 애니메이션) |
| Shelves | 오른쪽 벽 식재료 선반 뼈대 (빈 선반, 4칸 5단) |
| Storage_Cabinet | 오른쪽 앞 보관 캐비닛 |
| Front_Counter | 앞쪽 카운터 (소스/포장지/POS 올릴 자리) |

## 냉장고 문

- 문은 푸드트럭 앞쪽(어닝 방향, -Y)을 향함
- 두 문의 원점(pivot)이 오른쪽 힌지 위치 (x=6.38, y=2.88). 문 아래 높이는 위 문 z=1.14, 아래 문 z=0.09
- 열림 각도 100° (Blender 기준 Z축 회전). 스크립트에는 1~48프레임 키프레임, GLB에는 `*_OpenClose` 클립 2개
- 유니티에서 상호작용으로 열 거라면 스크립트로 문 오브젝트의 Y축 회전을 직접 돌리는 쪽이 편함. 좌표계가 달라 회전 방향이 반대(-100°)일 수 있음

## 사용법

1. Blender: Scripting 탭에서 `foodtruck_basic_blender.py` 열기 → Run Script. 다시 실행하면 이전 결과를 지우고 새로 생성
2. 스크립트 맨 위 옵션: `SHOW_ROOF`, `SHOW_HOOD`, `ADD_CAMERA_LIGHT`, `ANIMATE_DOORS`, `EXPORT_PATH`(.fbx / .glb 내보내기)
3. 유니티: GLB는 glTFast 패키지 필요. 그게 불편하면 `EXPORT_PATH`에 `.fbx`를 적어 내보내서 사용

## 알아둘 점

- 모든 오브젝트의 원점은 월드 (0,0,0)이고, 냉장고 문 2개만 힌지 위치에 있음
- 한 오브젝트 안에서 재질이 슬롯으로 나뉘어 있음. 문이나 서랍을 따로 움직이려면 Edit 모드에서 Separate(P) 필요
- Blender에서 직접 실행해 확인하지는 못했음. 스크립트는 가짜 bpy로 에러 없이 도는 것만 검증했고 GLB는 파일 구조만 확인함. 처음 열어보고 문제가 있으면 알려줄 것
- 베벨 노멀이 Blender 버전에 따라 적용 안 되면 오브젝트 선택 후 Shade Smooth
