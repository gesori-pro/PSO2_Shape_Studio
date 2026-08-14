# PSO2 Shape Studio

[English](README.md) | [日本語](README.ja.md) | **한국어**

PSO2 Shape Studio는 의상 체형 보정 AQM(`_sa.aqm`)을 이용해 PSO2 및 PSO2:NGS
캐릭터 체형을 후보정하는 사용자를 위한 Windows 데스크톱 도구입니다. Blender 없이
모델을 미리 보면서 체형 보정을 적용·편집·저장할 수 있습니다.

> **현재 공개 버전:** [1.0.5](https://github.com/gesori-pro/PSO2_Shape_Studio/releases/tag/v1.0.5).
> **다음 빌드:** 1.1.0.
> 최신 자체 포함 Windows x64 빌드는 릴리스 페이지에서 다운로드할 수 있습니다.

## 기능

- `.aqp`, `.aqn`, `.ice`에서 PSO2 모델을 열 수 있습니다.
- 구 PSO2의 종족별 캐릭터 커스터마이즈 파일(`.fdp`, `.fnp`, `.fhp`, `.fcp`)과
  암호화되지 않은 변형 파일을 불러와 체형과 색상을 적용할 수 있습니다.
- 의상 체형 보정 모션(`_sa.aqm`)을 불러오고 저장할 수 있습니다.
- `body_root` Y 스케일로 발바닥 높이를 0.500부터 1.200까지 0.001 단위로 조절하고,
  월드 Y=0 아래의 교차 부분을 확인할 수 있는 반투명 면 바닥 가이드와 비교할 수 있습니다.
- 스케일, 위치, 회전을 슬라이더 또는 직접 숫자 입력으로 편집할 수 있습니다. 숫자 칸에서는
  마우스 휠로 미세 조절할 수 있고, 위치는 0.001 단위이며 회전 슬라이더는 중앙 부근에서
  정확히 0으로 스냅됩니다.
- `Ctrl+Z`, `Ctrl+Y`, `Ctrl+Shift+Z`로 체형 편집을 실행 취소하거나 다시 실행할 수
  있습니다.
- 옵션 창에서 게임 데이터 폴더와 기본 메인·서브 피부색을 설정하고, 기본 체형 그룹을
  표시하거나 숨길 수 있습니다.
- AQN 스켈레톤이 포함된 모델을 불러오면 이름순 목록에서 편집할 본을 L/R 쌍 또는 단일 본
  그룹으로 추가할 수 있습니다. 기본 및 추가 그룹은 하나의 스크롤 목록에 함께 표시됩니다.
- 경계선을 드래그해 사이드바 너비를 조절할 수 있습니다. 너비는 다음 실행 때 복원되며,
  내부 영역을 스크롤해도 파일 작업 버튼은 항상 표시됩니다.
- 로컬 PSO2 게임 폴더를 지정하고 데이터 구조를 검증한 뒤 모델 검색 캐시를 갱신할 수
  있습니다.
- 이름, ID, 파일명, MD5로 의상 모델을 검색하고 베이스웨어, 세트웨어, 아우터웨어,
  클래식 코스튬(토탈웨어)만 표시할 수 있습니다.
- 선택한 세트웨어에 연결된 아우터웨어와 이너웨어를 자동으로 불러옵니다.
- 카탈로그 데이터가 있는 아이템은 영어(글로벌) 및 일본어 이름으로 검색할 수 있습니다.
- 로컬 게임 데이터에서 타입 1 및 타입 2 피부 텍스처를 선택할 수 있습니다.
- 의상을 잘 볼 수 있도록 뷰포트 배경색을 8종 중에서 선택할 수 있습니다.
- 지원되는 베이스웨어와 아우터웨어 모델에 포함된 장식 파츠를 표시하거나 숨길 수 있습니다.
- 애플리케이션 UI를 영어(글로벌), 일본어, 한국어, 중국어 번체로 전환할 수 있습니다.
- JSON 파일만으로 인터페이스 언어를 추가하거나 덮어쓸 수 있습니다. 자세한 방법은
  [현지화 참여 안내](LOCALIZATION.md)를 참고하세요.

## 요구 사항

- Windows x64
- 소스에서 빌드할 때
  [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- 모델 검색과 텍스처 자동 검색을 사용할 때 로컬에 설치된 PSO2 또는 PSO2:NGS

추출된 모델 파일은 게임 폴더를 설정하지 않고도 직접 열 수 있습니다.

## 기본 사용 순서

1. PSO2 Shape Studio를 실행합니다.
2. 메인 화면 또는 옵션 창에서 게임 설치 폴더, `pso2_bin` 또는 `pso2_bin/data`를
   선택합니다.
3. 캐시를 갱신하여 의상 모델을 검색하거나 추출된 모델 파일을 직접 엽니다.
4. 필요하면 캐릭터 파일이나 기존 `_sa.aqm` 체형 보정을 불러옵니다.
5. S/P/R 슬라이더를 편집하거나 숫자를 직접 입력합니다. 옵션 창에서는 기본 그룹을
   숨기거나 불러온 모델의 본을 추가할 수 있습니다. 필요할 때 편집을 실행 취소하거나
   초기화합니다.
6. 편집 결과를 `_sa.aqm` 파일로 저장합니다.

## 카메라 조작

| 입력 | 동작 |
| --- | --- |
| 왼쪽 또는 오른쪽 드래그 | 캐릭터 회전 |
| `Ctrl` + 드래그 | 카메라를 상하로 이동 |
| 마우스 휠 | 확대 또는 축소 |
| 휠 클릭 | 시점 초기화 |

## 소스에서 빌드

서브모듈을 포함하여 저장소를 복제합니다.

```powershell
git clone --recurse-submodules https://github.com/gesori-pro/PSO2_Shape_Studio.git
cd PSO2_Shape_Studio
```

서브모듈 없이 복제했다면 별도로 초기화합니다.

```powershell
git submodule update --init --recursive
```

x64 Release 구성으로 빌드하고 테스트합니다.

```powershell
dotnet build Pso2ShapeStudio.sln -c Release -p:Platform=x64
dotnet test Pso2ShapeStudio.sln -c Release -p:Platform=x64
```

소스에서 애플리케이션을 실행합니다.

```powershell
dotnet run --project src/App/Pso2ShapeStudio.App.csproj -c Release -p:Platform=x64
```

배포용 패키지를 빌드합니다.

```powershell
./publish.ps1
```

이 스크립트는 `src/App/Pso2ShapeStudio.App.csproj`에서 버전을 읽고 테스트를 실행한
뒤, 자체 포함 압축 파일을 `dist/`에 만듭니다.

## 게임 데이터와 개인정보

PSO2 게임 에셋, 추출된 모델, 텍스처 및 캐릭터 파일은 이 저장소에 포함되지
않습니다. 애플리케이션은 로컬 컴퓨터에서 사용자가 선택한 파일을 읽으며 게임 데이터를
업로드하지 않습니다.

## 의존성과 크레딧

- [PSO2-Aqua-Library](https://github.com/Shadowth117/PSO2-Aqua-Library)는 PSO2 형식과
  ICE 데이터를 처리하는 데 필요한 소스 의존성입니다.
- 내장된 영어·일본어 아이템 이름 표는 PSO2NGS Mod Manager 아이템 데이터로부터
  생성되었습니다.

## 라이선스

PSO2 Shape Studio는 GNU General Public License version 3 조건에 따라 배포됩니다.
자세한 내용은 [LICENSE](LICENSE)를 참고하세요.
