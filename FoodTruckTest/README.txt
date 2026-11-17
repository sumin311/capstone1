푸드트럭 음식 테스트 씬 사용법
================================
푸드트럭(FoodTruck_Models) 안에 음식 모델 43종(타코야끼/샌드위치/꼬치구이/크레페)을
자동으로 배치한 테스트 씬을 만들어 줍니다.

Unity에 넣을 것 (모두 Assets 폴더 아래 아무 곳)
1. 이 FoodTruckTest 폴더 통째로
2. 푸드트럭: FoodTruck_Models/foodtruck_basic.glb
   - .glb를 읽으려면 UniGLTF(UniVRM에 포함) 또는 glTFast 패키지가 필요합니다.
   - 둘 다 없으면 foodtruck_basic_blender.py의 EXPORT_PATH를 .fbx로 지정해 FBX로 뽑아 넣으세요.
3. 음식: 각 폴더의 fbx 폴더만 (Takoyaki_Models/fbx, Sandwich_Models/fbx, Yakitori_Models/fbx, Crepe_Models/fbx)
   - fbx 폴더 안의 .png 텍스처도 같이 들어가야 무늬/라벨이 붙습니다.
   - .blend 파일은 넣지 마세요. (Unity가 Blender로 변환하려고 해서 느려지고 에러가 날 수 있음)

씬 만들기
- 상단 메뉴 Tools > FoodTruck Test > Create Test Scene
  -> Assets/FoodTruckTest/FoodTruckTestScene 이 만들어지고 열립니다. Play!
- VRMTest 스크립트와 .vrm 이 프로젝트에 있으면 아바타가 트럭 안에 서고, 걸어 다닐 수 있습니다.
  (조작은 VRMTest와 같음. 트럭에 MeshCollider를 자동으로 붙여서 벽/카운터를 통과하지 않음)
- 아바타 없이 둘러보려면 Tools > FoodTruck Test > Create Test Scene (Free Camera)

자유 카메라 조작
- 마우스 오른쪽 드래그: 시점 회전
- WASD: 이동, Q/E: 아래/위, Shift: 빠르게, 휠: 앞뒤, H: 도움말 숨기기

배치 위치
- 화구 카운터: 타코야끼 그릴 2개, 샌드위치 프레스(+토스트), 꼬치구이 그릴 2개, 크레페 기계(+반죽 시트)
- 조리대: 반죽/재료 그릇 7개
- 앞 카운터: 완성 요리 4종, 콘 크레페, 소스 병/양념/초콜릿 병/짤주머니
- 오른쪽 앞 보관함 위: 식빵, 바나나, 버터
- 오른쪽 선반 2~4단: 햄/치즈/상추/식빵/대파, 꼬치 4종, 타코야끼 낱개/파 토막
- Hierarchy의 Foods 아래에 구역별로 묶여 있습니다.

참고
- 음식은 모두 트럭 안쪽(통로)을 보도록 돌려 놓습니다. 라벨이 벽 쪽을 보고 있으면
  FoodTruckTestSceneBuilder.cs 맨 위의 ItemYawOffset 값을 180으로 바꾸고 다시 만드세요.
- 푸드트럭 모델을 못 찾으면 같은 크기의 회색 박스로 대신 만들고 알려 줍니다.
- 못 찾은 음식 모델이 있으면 창으로 목록을 보여 줍니다.
- URP 프로젝트에서 음식 재질이 분홍색이면 Edit > Rendering > Materials > Convert... 로 변환하세요.
- 메뉴를 다시 실행하면 씬을 새로 만들어 덮어씁니다. (씬에서 직접 옮긴 내용은 사라짐)
