# Unity 2D 플랫포머 멀티플레이

포트폴리오용 2인 코옵 2D 플랫포머 전투게임. Netcode for GameObjects 기반.
**게임플레이 네트코드를 직접 구현했음을 증명하는 것이 목적입니다.**

설계 결정, 아키텍처, 권한 모델, 진행 상태는 `docs/project_context.md` 참조.
해당 문서가 단일 진실 원천이며, 이 파일과 충돌하면 그쪽을 따릅니다.

## 작업자 배경

C++/DirectX 9,11로 자체 엔진을 여러 개 만든 경험 보유. 렌더링 파이프라인, 물리 충돌,
HFSM, 에디터 툴을 직접 구현함. Unity와 C#은 이번이 첫 프로젝트.

게임 프로그래밍 기초 설명은 불필요합니다. C++ 및 네이티브 엔진 개념에 대응시켜
설명하면 이해가 빠릅니다.

---

## AI 활용 원칙

AI는 도구로 활용합니다. 다만 **작업자가 설명하지 못하는 코드가 저장소에 남으면 안 됩니다.**
면접에서 네트코드는 깊게 파고드는 주제라 한 줄씩 근거를 댈 수 있어야 합니다.

### 핵심 영역 — 설명 먼저, 코드는 그다음

- 고정 틱 시뮬레이션 루프
- 입력 버퍼 및 시퀀스 번호 관리
- 클라이언트 예측 / 서버 재조정
- 스냅샷 보간 / 랙 보상 rewind
- BoxCast 기반 캐릭터 컨트롤러

이 영역은 원리와 설계 선택지를 먼저 제시하고, 작업자가 방향을 정한 뒤 구현으로 넘어갑니다.
작업자가 코드부터 요청하면 제공하되, **왜 그 형태가 되는지 근거를 함께** 씁니다.
파일에 바로 쓰기 전에 접근 방식을 한 번 확인받으세요.

작업자가 직접 쓴 코드 리뷰는 적극적으로 하세요. 특히 되감기 시작 틱 오프셋, 버퍼 인덱싱,
결정성 위반, 매 프레임 GC 할당을 중점적으로 봅니다.

### 바로 진행해도 되는 영역

에디터 확장, 디버그 오버레이, 개발 툴, 스냅샷 직렬화 보일러플레이트, 링버퍼 등 자료구조,
테스트 하네스, 결정성 검증 스크립트, 리팩터링, 문서화.

---

## 유니티 파일 취급

건드리면 프로젝트가 깨지는 파일들입니다.

- `*.meta` 직접 편집 및 삭제 금지. GUID 참조가 깨집니다
- `*.prefab`, `*.unity`, `*.asset` YAML 수동 편집 금지
- `Library/`, `Temp/`, `obj/`, `Logs/`, `UserSettings/` 는 생성물. 읽지도 쓰지도 마세요
- `Packages/manifest.json` 은 읽기만. 패키지 추가는 Package Manager UI로 안내
- `ProjectSettings/` 변경은 사전 확인 요청

프리팹 배선, 씬 구성, Animator 상태 설정, Inspector 참조 연결은 작업자가 직접 합니다.
해당 작업이 필요하면 코드가 아니라 **에디터에서 수행할 절차**로 안내하세요.

---

## Git 운용

상세 규칙은 `docs/project_context.md` 10번 참조.

### Claude Code가 하는 것

- `gh` CLI로 이슈, 마일스톤 생성 및 조회
- 브랜치 생성 및 전환 (`git switch -c`, `git switch`)
- 상태 확인 (`git status`, `git diff`, `git log`, `git branch`)

### Claude Code가 하지 않는 것

**아래 명령은 절대 실행하지 마세요.** 작업자가 직접 수행합니다.

- `git add`, `git commit`, `git push`
- `git merge`, `git rebase`, `git reset`, `git restore`, `git checkout -- <file>`
- `git stash`
- `gh pr create`, `gh pr merge`
- 이슈 종료 (`gh issue close`)

커밋할 시점이 되면 **커밋 메시지 초안만 제시**하고 멈추세요.
직접 실행하거나, 실행하겠다고 제안하지 마세요.

```
작업 완료. 아래로 커밋하시면 됩니다.

feat(sim): 수평 이동 캐스트 및 충돌 보정 구현

Refs #12

변경 파일:
  Assets/Scripts/Simulation/CharacterController2D.cs (+84)
  Assets/Scripts/Simulation/CharacterController2D.cs.meta (신규)
  Assets/Scripts/Core/CastResult.cs (+22)
```

`.cs` 신규 생성 시 `.cs.meta`는 유니티 에디터가 포커스를 받아야 만들어집니다.
**에디터를 한 번 활성화한 뒤 커밋하도록 안내**하세요. meta 누락은 GUID 참조를 깨뜨립니다.

### 이슈와 브랜치

- 작업 시작 시 대응 이슈가 없으면 **먼저 이슈 생성을 제안**하고 확인받으세요
- 이슈 제목과 본문 초안을 보여준 뒤 승인받고 `gh issue create` 실행
- 이슈 생성 후 브랜치까지 이어서 생성: `<유형>/<이슈번호>-<짧은-영문-설명>`
- 브랜치 생성 전 **현재 브랜치가 `main`이고 working tree가 clean한지 확인**.
  아니면 작업자에게 알리고 중단하세요
- 마일스톤은 현재 + 다음 것까지만. 미리 대량 생성 금지

---

## 코드 컨벤션

- 클래스, 메서드, 프로퍼티, 상수: PascalCase
- private 필드: `_camelCase` (`[SerializeField] private` 포함)
- 지역 변수, 파라미터: camelCase
- 인터페이스: `I` 접두사
- `var`는 우변에서 타입이 자명할 때만
- 주석은 한글. **특수문자와 이모지 사용 금지**
- 코드 작성 시 어느 계층(Input/Simulation/Presentation/Network)에 속하는지 명시
- 매 프레임 호출 경로에서 GC 할당 회피. `GetComponent` 캐싱, 문자열 연결 주의

어셈블리 참조 방향은 단방향입니다. 역방향 참조를 만드는 변경은 하지 마세요.

```
Network      -> Simulation -> Core
Presentation -> Simulation -> Core
Input        -> Core
```

Simulation 어셈블리에서 `Time.deltaTime`, `Time.time`, `UnityEngine.Random`,
`Transform`, `Animator`, `Rigidbody2D` 참조 금지. 상세는 `docs/project_context.md` 3번.

---

## 편의 기능으로 우회 금지

| 금지 | 대신 |
|---|---|
| Rigidbody2D 기반 이동 | BoxCast 커스텀 kinematic 컨트롤러 |
| NetworkTransform을 최종 해법으로 제시 | 직접 만든 스냅샷 동기화 + 보간 |
| Photon Fusion, Mirror로 전환 제안 | NGO 유지 |
| Relay, Lobby, 매치메이킹 | 직접 IP 접속 |
| Netcode for Entities, DOTS | NGO(GameObjects) 유지 |
| 고정소수점 수학 도입 | float 사용 |

학습 목적의 비교 설명은 예외입니다.

---

## 명령어

```bash
# 유니티 경로는 설치 후 실제 경로로 교체할 것
# UNITY="C:/Program Files/Unity/Hub/Editor/<version>/Editor/Unity.exe"

# 컴파일 검증
# "$UNITY" -batchmode -quit -projectPath . -logFile -

# EditMode 테스트
# "$UNITY" -batchmode -runTests -testPlatform EditMode -projectPath . -logFile -
```

멀티플레이 테스트는 MPPM(Multiplayer Play Mode) 가상 플레이어를 사용합니다. 빌드 불필요.

---

## AI 협업 로그

AI 도구 활용 경험도 포트폴리오 소재입니다.

제안한 코드나 진단이 **틀렸다는 것이 밝혀지면**, 수정 후 `docs/ai-collab-log.md`에
항목 추가를 제안하세요. 설계 판단 오류, 네트코드 로직 오류, 결정성 위반, 성능 함정만
기록합니다. 오타나 단순 컴파일 에러는 제외.

---

## 참조 문서

- `docs/project_context.md` — 설계 결정 근거, 아키텍처, 진행 상태, 확정 수치
- `docs/checklist/` — 주차별 작업 항목
- `docs/ai-collab-log.md` — AI 출력 오류 및 수정 기록

## 답변 규칙

- 한국어
- 간결하게. 과도한 수사 없이
- 모호한 상황에서 동의하지 말 것. 여러 옵션 + 트레이드오프 + 명확한 추천
- 현재 주차 목표를 벗어나는 스코프 확장 제안 금지
