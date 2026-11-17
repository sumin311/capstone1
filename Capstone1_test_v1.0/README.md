# Capstone1_test_v1.0 (Unity 프로젝트)

캡스톤1 푸드트럭 게임의 Unity 프로젝트입니다. 이 폴더가 Unity 작업의 원본입니다.

- Unity 버전: **2022.3.62f3** (Built-in 렌더 파이프라인, Linear)
- 패키지: UniVRM / UniGLTF (`Packages/com.vrmc.*`에 포함되어 있어서 따로 설치할 필요 없음)

## 처음 여는 법

1. 저장소를 받는다 (GitHub Desktop에서 Clone 또는 Pull).
2. Unity Hub → 프로젝트 → **추가 ▾ → 디스크에서 프로젝트 추가** → 이 `Capstone1_test_v1.0` 폴더 선택.
3. 처음 열 때는 `Library` 폴더를 새로 만드느라 몇 분 걸린다. (Library는 git에 안 올라감)
4. `Assets/Chef24MVP/Chef24MVP.unity`를 열고 Play. 씬이 이상하면 **Tools > Chef24 > Build MVP Scene**으로 다시 만든다.

## 폴더

| 폴더 | 내용 |
|---|---|
| `Assets/Chef24MVP` | 게임 MVP (기숙사 1일차 타코야끼). 설명은 그 안의 README.txt |
| `Assets/FoodTruckTest` | 푸드트럭 + 음식 43종 배치 테스트 씬, 손님 아바타 애니메이션 구조 |
| `Assets/VRMTest` | VRM 아바타 테스트 씬 |
| `Assets/Foods`, `Assets/FoodTruck` | 저장소 루트 `*_Models` 폴더에서 가져온 FBX / GLB |
| `Assets/test1_restored.vrm` | 손님 NPC로 쓰는 임시 캐릭터 |

## Mixamo 애니메이션 (각자 받아서 넣기)

Mixamo 원본 FBX는 공개 저장소에 다시 배포하면 안 돼서 git에서 뺐습니다 (`.gitignore`).
손님이 걷는 모션이 필요하면:

1. [mixamo.com](https://www.mixamo.com)에서 Idle, Walking, Running을 **FBX for Unity / Without Skin / In Place**로 받는다.
2. `Assets/VRMTest/Animations/`에 `Idle.fbx`, `Walking.fbx`, `Running.fbx` 이름으로 넣는다.
3. **Tools > FoodTruck Test > Rebuild Avatar Animations** 실행.

없어도 게임은 돌아가고, 손님이 걷는 동작 없이 미끄러지듯 움직입니다.

## 주의

- 모델(.blend, Blender 스크립트)의 원본은 저장소 루트의 `*_Models` 폴더입니다. 모델을 고치면 FBX를 다시 뽑아 `Assets/Foods/...`에 덮어쓰세요.
- 같은 씬(.unity)을 두 사람이 동시에 고치면 합치기 어렵습니다. 씬 작업은 미리 말하고 한 명씩 하거나, 각자 테스트용 씬을 따로 만들어 쓰세요.
