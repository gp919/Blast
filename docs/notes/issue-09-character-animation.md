# 이슈 #9: 캐릭터 애니메이션과 Sorting Layer

날짜: 2026-08-27
브랜치: `feat/9-character-animation`

2주차 C 의 문제 상황 영상을 촬영하기 전에, 캐릭터가 idle / run / jump / fall 상태로
보이도록 만드는 작업입니다. 흰 사각형이 끊기며 움직이는 것보다 캐릭터가 끊기며
움직이는 것이 입력 지연을 훨씬 잘 보여주고, 그 영상은 한 번만 촬영할 수 있기 때문에
촬영 전에 마쳐야 했습니다.

중간에 캐릭터 애셋을 SPUM 으로 정했다가 Pixel Adventure 로 되돌렸습니다. 그 과정에서
내린 판단이 이 문서의 절반을 차지합니다.

---

## 1. 코드 리뷰

### 실제 결함: 튜닝 값이 애셋에 반영되지 않았음

`CharacterTuningAsset.cs` 의 `[SerializeField]` 초기화 식을 새 값으로 바꿨지만,
`Assets/Settings/CharacterTuning.asset` 은 여전히 이전 값을 그대로 저장하고 있습니다.

```
코드 기본값   이동 1.12  중력 -9.14  점프 3.20  종단 -6.40
애셋 실제값   이동 5     중력 -30    점프 8     종단 -20
```

**직렬화 필드의 초기화 식은 인스턴스를 새로 만들 때만 실행됩니다.** 이미 만들어진
애셋은 자기 파일에 값을 저장하고 있어서 코드를 고쳐도 바뀌지 않습니다. C++ 에
대응시키면, 생성자의 기본 인자를 바꾼 뒤에 이미 디스크에 저장된 설정 파일까지
갱신되기를 기대한 것과 같습니다.

충돌 박스는 `0.24 x 0.32` 로 반영되었는데 속도만 이전 값이라, 캐릭터 키를 기준으로
환산하면 이동 속도가 **초당 키의 15.6배**이고 점프 정점이 **키의 3.3배**입니다.
인스펙터의 튜닝 진단이 경고를 표시하고 있습니다.

- 상태: **미수정.** Inspector 에서 애셋을 직접 고쳐야 합니다
- 이슈 #9 의 완료 조건에는 튜닝이 포함되지 않으므로 이번 이슈를 막지는 않지만,
  **#10 촬영 전에는 반드시** 정리해야 합니다

### 되돌린 것: SPUM 전용 구조

SPUM 유닛은 스프라이트가 파츠 수십 개로 나뉘어 있어서 `SpriteRenderer.flipX` 가
제대로 동작하지 않습니다. 각 파츠가 제자리에서 뒤집힐 뿐 배치가 거울상이 되지
않습니다. 그래서 한때 `_visualRoot` 를 두고 `localScale.x` 의 부호로 전체를 한 번에
좌우 반전했고, 바운드도 자식 렌더러 전체의 합집합으로 계산했습니다.

Pixel Adventure 로 되돌아오면서 그 구조를 전부 제거했습니다. 스프라이트가 한 장이면
`flipX` 로 충분하고, 바운드도 `SpriteRenderer.bounds` 하나로 계산할 수 있습니다.

**남긴 것**은 SPUM 과 무관한 부분입니다.

- Animator 파라미터를 상태에서 파생시키는 경로
- 원격 캐릭터 속도 추정
- 커스텀 인스펙터의 진단 패널

### 유지할 판단: 원격 애니메이션을 전송 필드로 해결하지 않은 것

원격 캐릭터는 위치와 방향만 전송받습니다. `Velocity` 와 `IsGrounded` 는 스폰 시점의
값에 멈춰 있어서, 그 값을 그대로 Animator 에 전달하면 캐릭터가 움직이는 중에도
idle 상태로 고정됩니다.

`NetworkVariable` 에 속도와 접지를 추가하면 간단히 해결되지만 그렇게 하지 않았습니다.
**이슈 #10 이 기준선 대역폭을 측정하기 직전**이라, 지금 전송 필드를 늘리면 3주차
최적화 전후를 비교하는 기준이 오염됩니다. 애니메이션은 원래 Presentation 계층의
파생물이고 시뮬레이션 결과를 바꾸지 않으므로, 화면에 반영된 결과를 다시 읽어서
추정하는 쪽이 계층 관점에서도 맞습니다.

### 사소한 것 셋

- Sorting Layer 이름이 `BackGround` 입니다. `Background` 가 맞지만 동작에는 영향이
  없고, 이름을 바꿔도 uniqueID 로 참조가 유지됩니다. 지금 수정해도 되고 나중에
  수정해도 됩니다
- 에디터 도구가 `serializedObject.FindProperty("_spriteRenderer")` 처럼 필드 이름을
  문자열로 찾습니다. 필드 이름을 바꾸면 오류 없이 null 이 됩니다. `_boxSize` 만은
  찾지 못했을 때 `Debug.LogError` 를 출력하도록 해두었습니다
- `Assets/Settings/InputSystem_Actions.inputactions` 와 `ProjectSettings.asset` 에
  Unity 가 포맷을 갱신한 변경이 섞여 있습니다(`priority: 0` 추가 등). 의미 없는
  변경이므로 커밋에 포함해도 문제가 없습니다

---

## 2. Unity 와 C# 을 자체 엔진 개념으로

### Animator 는 상태 기계이지 재생기가 아니다

`Animator.Play("Jump")` 같은 명령형 호출을 쓰지 않았습니다. 대신 파라미터 세 개를
매 프레임 전달하고, 전이 조건은 컨트롤러 애셋이 판단합니다.

```
코드          Animator          결과
SetFloat      Speed 1.12   ->   IsGrounded true && Speed > 0.1  ->  Run 상태
SetBool       IsGrounded true
SetFloat      VerticalVelocity -3.2
```

자체 엔진의 HFSM 과 대응시키면, **전이 테이블을 코드가 아니라 애셋이 보관하는**
형태입니다. 상태 전이 규칙을 바꾸는 데 재컴파일이 필요 없다는 것이 이득이고,
대신 이름의 오타가 컴파일 시점에 검출되지 않는다는 것이 대가입니다.
`SetFloat("Sped", ...)` 는 예외도 경고도 없이 무시됩니다. 그래서 커스텀 인스펙터가
**Animator 의 실제 파라미터 목록을 전부 나열**하고 코드가 사용하는 이름과 대조합니다.

### 트리거를 쓰지 않는 이유는 재조정이다

Animator 에는 `SetTrigger` 라는 일회성 신호가 있고 점프 같은 이벤트에 자연스러워
보입니다. 그래도 사용하지 않았습니다.

3주차 재조정이 들어오면 서버 보정을 받을 때마다 과거 틱부터 다시 시뮬레이션됩니다.
이벤트 기반이면 되감기 구간의 점프마다 트리거가 다시 발동되어, 한 번만 실행한
점프인데도 모션과 효과음이 여러 번 나옵니다. RTT 150ms 에 60Hz 면 보정할 때마다
9틱이 다시 실행되므로 이 현상이 계속 발생합니다. **상태에서 파생시키면 되감기가 몇
번이든 최종 결과가 같습니다.**

멱등성(idempotence)의 문제입니다. `SetBool(x)` 는 몇 번을 호출해도 결과가 같지만,
`SetTrigger()` 는 호출한 횟수에 따라 결과가 달라집니다.

### 스프라이트 애니메이션은 PPtr 커브다

`.anim` 파일을 열어보면 구조가 그대로 드러납니다.

```yaml
m_PPtrCurves:
- curve:
  - time: 0
    value: {fileID: -6367560908665664636, guid: 3750f45c..., type: 3}
  - time: 0.05
    value: {fileID: -6056987297122131021, guid: 3750f45c..., type: 3}
  attribute: m_Sprite
  path:
  classID: 212
```

시간축에 **오브젝트 참조(PPtr = Pointer)** 를 기록해 둔 배열입니다. float 커브가
아니라 포인터 커브라 보간이 없고 계단식으로 바뀝니다. `classID: 212` 는
SpriteRenderer 를 가리키고, `path:` 가 비어 있으면 Animator 가 붙은 오브젝트 자신을
가리킵니다.

자체 엔진에서 프레임 인덱스 배열을 두고 타이머로 다음 프레임을 넘기던 구조와
같으며, 인덱스 대신 애셋 GUID 와 로컬 fileID 의 쌍을 저장한다는 점만 다릅니다.

이 구조를 알면 이번에 겪은 버그가 파일 한 줄로 설명됩니다.

```yaml
    - time: 0
      value: {fileID: 0}     # null
```

`fileID: 0` 은 null 참조입니다. 클립이 매 프레임 `SpriteRenderer.sprite` 에 null 을
대입하고 있었고, 그래서 캐릭터가 화면에서 사라졌습니다. 이동은 Transform 이
담당하기 때문에 그동안에도 정상적으로 동작하고 있었습니다.

### Sprite Mode = Multiple 은 슬라이스가 없으면 스프라이트가 0개다

앞에서 확인한 null 의 원인입니다. `Jump (32x32).png` 를 Single 에서 Multiple 로
바꾸고 슬라이스를 하지 않으면 `.meta` 의 `sprites: []` 가 비어 있게 됩니다.

- **Single**: 텍스처 전체가 스프라이트 1개. 자동 생성되며 fileID 는 관례적으로 `21300000`
- **Multiple**: 슬라이스한 사각형마다 스프라이트 1개. **슬라이스가 없으면 0개**

스프라이트가 0개인 텍스처를 Animation 창에 드래그하면 키프레임은 만들어지지만 값이
null 이 됩니다. Project 창에서 텍스처를 펼쳤을 때 아무것도 표시되지 않는 것이 그
상태를 알려주는 신호입니다.

C++ 에 대응시키면, 텍스처 아틀라스는 로드했는데 UV 사각형 테이블이 비어 있는
상태에서 "0번 스프라이트"를 요청한 것과 같습니다.

### 직렬화된 필드의 기본값은 기존 애셋에 소급되지 않는다

1번에서 다룬 결함이 이것입니다. `[SerializeField] private float _x = 5f;` 에서 `5f` 는
**새 인스턴스를 만들 때만** 적용됩니다. 이미 디스크에 저장된 애셋과 씬 오브젝트는
자기 파일에 기록된 값을 사용합니다.

같은 이유로 필드를 새로 추가하면 기존 애셋에도 그 필드의 초기화 값이 적용되지만,
이미 있는 필드의 초기화 식을 바꾸는 것은 아무런 효과가 없습니다.

### Sorting Layer 는 렌더 순서지 좌표가 아니다

2D 는 깊이 버퍼로 순서를 결정하지 않습니다. `Sorting Layer` → `Order in Layer` →
카메라 거리 순으로 정렬합니다. Z 좌표를 조정하는 대신 레이어 이름을 정하는 것이
Unity 2D 의 방식입니다.

파츠가 여러 개인 캐릭터(SPUM 이 그런 경우입니다)에는 `SortingGroup` 이라는 컴포넌트가
있어서 자식 전체를 하나의 단위로 정렬합니다. Pixel Adventure 는 스프라이트가 한
장이라 필요하지 않습니다.

### 픽셀 아트의 임포트 설정은 취향이 아니라 요구사항이다

| 설정 | 값 | 지키지 않으면 |
|---|---|---|
| Filter Mode | Point | 픽셀 경계가 보간되어 도트가 흐려짐 |
| Compression | None | 블록 압축으로 색이 번지고 알파 값이 손상됨 |
| Generate Mip Maps | 꺼짐 | 축소할 때 흐린 밉맵이 선택됨 |
| Pixels Per Unit | 100 (통일) | 애셋마다 월드 크기가 달라짐 |

PPU 는 "몇 픽셀을 1 유닛으로 볼 것인가"를 정하는 값입니다. 자체 엔진에서 월드
단위와 텍셀 단위의 환산 상수를 정하던 것과 같으며, **한 번 정하면 모든 애셋이
따라야 하는 축척**입니다.

---

## 3. 스크립트 파일별 설명

### 상태 하나가 화면에 반영되기까지의 경로

```
  Simulation                     Game                        Presentation
  ----------                     ----                        ------------
  SimulationWorld.Step
      |
      v
  PlayerState                TickDriver.RenderAll
   Position                      |
   Velocity        ------------->|  simulatedHere ?
   IsGrounded                    |     yes -> (prev, cur, alpha, true)
   FacingDirection               |     no  -> (cur,  cur, 0f,    false)
                                 |
                                 v
                        PlayerPresenter.Render
                                 |
                 +---------------+----------------+
                 |               |                |
                 v               v                v
          transform.position  flipX        Animator 파라미터
          (알파 보간)      (FacingDirection)  Speed / IsGrounded / VerticalVelocity
                                                  |
                                       simulatedHere 면 상태에서 직접
                                       아니면 렌더 위치 델타로 추정
```

마지막 인자 `stateIsSimulatedHere` 가 이번 이슈의 핵심입니다. 상태의 어느 필드까지
믿어도 되는지를 알리는 신호입니다.

### 수정한 파일

#### `Presentation/IPlayerPresenter.cs`: 계약에 신뢰 범위를 추가

```csharp
void Render(in PlayerState previous, in PlayerState current, float alpha, bool stateIsSimulatedHere);
```

인자가 하나 늘었습니다. 참이면 이 피어가 직접 시뮬레이션한 결과라 `Velocity` 와
`IsGrounded` 가 유효한 값이고, 거짓이면 서버에서 받은 위치와 방향만 채워져 있습니다.

**상태를 두 종류의 타입으로 나누는 대신 플래그로 알리는 이유**는, 이 구분이 상태의
성질이 아니라 "지금 이 피어에서 누가 시뮬레이션하는가"라는 호출 시점의 조건이기
때문입니다. F3 으로 권한 모드를 바꾸면 같은 캐릭터가 같은 프레임에 반대 경우로
바뀝니다.

#### `Presentation/PlayerPresenter.cs`: 파생 로직이 모두 모여 있는 곳

세 가지 일을 합니다. 위치를 반영하고, 좌우를 반전시키고, Animator 파라미터를
파생시킵니다.

```csharp
private static readonly int _speedParameter = Animator.StringToHash("Speed");
```

문자열을 매 프레임 전달하면 Animator 가 내부에서 해시를 다시 계산합니다. 이름은
컴파일 시점에 정해져 있으므로 한 번만 계산해 둡니다. 매 프레임 실행되는 경로에서
GC 할당과 문자열 비교를 함께 제거하는 관용적인 방법입니다.

```csharp
_accumulatedDisplacement += renderPosition - _lastRenderPosition;
_accumulatedSeconds += Time.deltaTime;
if (_accumulatedSeconds < EstimationWindowSeconds) return;
_estimatedVelocity = _accumulatedDisplacement / _accumulatedSeconds;
```

원격 캐릭터의 속도를 추정하는 코드입니다. **프레임마다 미분하지 않고 0.1초 구간으로
누적한 뒤에 나눕니다.** 원격 위치는 30Hz 로 도착하지만 렌더링은 매 프레임
수행됩니다. 프레임 사이의 위치 차이를 dt 로 나누면 값이 도착한 프레임에서만 큰 값이
나오고 나머지 프레임에서는 0 이 되므로, 초당 30번씩 idle 과 run 이 번갈아
나타납니다. 구간 단위로 누적하면 그 구간의 평균 속도를 얻을 수 있습니다.

```csharp
if (Mathf.Abs(verticalVelocity) > AirborneSpeedThreshold) _airborneHoldRemaining = AirborneHoldSeconds;
else if (_airborneHoldRemaining > 0f) _airborneHoldRemaining -= Time.deltaTime;
return _airborneHoldRemaining > 0f;
```

원격 캐릭터는 접지 여부도 전송받지 않으므로 수직 속도로 추정합니다. **점프 정점에서
수직 속도가 순간적으로 0 을 지나기 때문에** 그대로 판정하면 정점마다 착지 모션이 한
번씩 나타납니다. 공중 판정을 80ms 동안 유지해서 그 구간을 넘깁니다. 이 값은 중력
크기를 기준으로 임계값 0.5 를 통과하는 데 걸리는 시간을 계산해서 얻었습니다.

#### `Game/TickDriver.cs`: 호출부에서 신뢰 범위를 명시

```csharp
entry.Presenter.Render(entry.PreviousState, entry.State, alpha, true);   // 직접 시뮬레이션한 것
entry.Presenter.Render(entry.State, entry.State, 0f, false);             // 전송받은 것
```

이 두 줄이 이번 이슈에서 바뀐 전부입니다. 아래쪽 줄이 3주차에 스냅샷 보간이 들어갈
위치이고, 마지막 인자가 거짓인 것이 "이 상태는 위치와 방향만 채워져 있다"는
선언입니다.

#### `Game/CharacterTuningAsset.cs`: 기본값과 주석

기본값을 Pixel Adventure 캐릭터 기준으로 바꾸고, **초기화 식이 기존 애셋에
소급되지 않는다**는 경고를 주석으로 남겼습니다. 이번에 실제로 겪은 함정입니다.

### 신규 파일

#### `Editor/PlayerPresenterEditor.cs`: 관측 수단이자 축척 계산기

빌드에 포함되지 않는 에디터 전용 코드입니다. 세 가지 기능을 제공합니다.

**1. 애니메이션이 바뀌지 않을 때 원인을 구분하기**

원인이 세 가지인데 화면만 봐서는 구분되지 않습니다. Animator 가 연결되지 않았는지,
파라미터 이름이 다른지, 전이 조건이 맞지 않는지 알 수 없습니다. 앞의 두 가지는
예외도 경고도 발생하지 않고 무시되므로, 인스펙터에서 직접 확인할 수단이
필요했습니다.

```csharp
AnimatorControllerParameter[] parameters = animator.parameters;
```

**파라미터를 이름으로 조회하지 않고 전체 목록을 나열하는 것이 핵심입니다.** 코드가
사용하는 이름으로 조회하면 이름이 틀렸을 때도 0 이 반환되어 "값이 전달되지
않는다"로만 보입니다. 실제 목록을 함께 표시해야 오타가 드러납니다.

**2. 충돌 박스를 스프라이트에서 파생**

```csharp
Vector2 boxSize = new Vector2(bounds.size.x * _boxWidthRatio, bounds.size.y * _boxHeightRatio);
SerializedObject serializedAsset = new SerializedObject(tuningAsset);
serializedAsset.FindProperty("_boxSize").vector2Value = boxSize;
serializedAsset.ApplyModifiedProperties();
```

`SerializedObject` 로 값을 쓰면 Undo 등록과 애셋 더티 표시가 함께 처리됩니다. 필드에
직접 대입하면 에디터가 변경 사실을 인식하지 못해 저장되지 않습니다.

폭 비율의 기본값이 1 이 아닌 이유가 있습니다. 스프라이트 바운드는 잘라낸 픽셀이
아니라 **슬라이스한 칸 전체**라 투명 여백이 포함되고, 팔이나 머리카락은 벽에 닿아도
문제가 없습니다. 여백까지 충돌 폭에 포함시키면 화면상으로는 벽과 거리가 남아 있는데도
캐릭터가 멈춰 섭니다.

**3. 튜닝 값을 캐릭터 키로 나누어 확인하기**

```
점프 정점       0.56  =  키의 1.75 배   (권장 1.5 ~ 2.0)
정점 도달 시간  0.35 초                 (권장 0.30 ~ 0.40)
이동 속도       1.12/s =  초당 키의 3.5 배 (권장 3 ~ 4)
```

절대 수치는 축척이 바뀌면 의미가 사라지지만 비율은 그대로 유지됩니다. "초당 5 유닛"은
캐릭터가 커지면 느려지고 작아지면 빨라지지만, "초당 키의 3.5배"는 축척과 무관하게
같은 조작 감각을 나타냅니다.

**정점 도달 시간만은 비율이 아니라 절대 시간**입니다. 사람 손의 반응 속도에 좌우되는
값이라 캐릭터 크기와 무관합니다. 그래서 목표 두 개(정점 높이 = 키의 배수, 도달
시간 = 절대 초)에서 중력과 점프 속도를 함께 역산합니다.

```
h = g * t^2 / 2  ->  g = 2h / t^2
v = g * t        ->  v = 2h / t
```

중력을 고정한 채 점프 속도만 조정하면, 캐릭터 크기가 바뀌었을 때 점프가 지나치게
빠르거나 지나치게 느리게 느껴집니다. 이번 축척 변경(키 0.5 에서 0.32 로)에서 중력이
-30 에서 -9.14 로 낮아진 것이 그 계산의 결과입니다.

### 수정하지 않은 파일 (전체 목록)

| 파일 | 역할 |
|---|---|
| `Core/PlayerState` | 한 틱의 시뮬레이션 상태. 애니메이션 파라미터의 원본 |
| `Core/InputCommand` | 틱 단위 입력 |
| `Core/FixedTickAccumulator` | 순수 누산기 |
| `Core/SimulationConstants` | 틱레이트 60Hz, 프레임당 최대 5틱 |
| `Simulation/CharacterController2D` | BoxCast 커스텀 kinematic 컨트롤러 |
| `Simulation/CharacterTuning` | 튜닝 구조체. `Default` 는 대체값이자 테스트 기준선 |
| `Simulation/SimulationWorld` | 틱 진행 진입점 |
| `Input/KeyboardInputSource` | 폴링 + 에지 래치 |
| `Network/*` | 스폰, 소유권, 입력 RPC, 위치 발행 |
| `Game/ConnectionHud` | F1 표시, F2 소유권, F3 권한 모드 |
| `Editor/TickDriverEditor` | 틱과 네트워크 상태 읽기 전용 표시 |

---

## 4. 이 이슈에서 겪은 것

### 애셋 방향을 한 번 되돌렸다

SPUM 을 선택했다가 Pixel Adventure 로 되돌아왔습니다. 되돌리는 비용이 컸던 부분은
코드가 아니라 **구조에 대한 가정**이었습니다.

- SPUM: 파츠 수십 개 → 부모 스케일의 부호로 미러링, 자식 렌더러 합집합 바운드,
  Animator 가 손자 오브젝트(`UnitRoot`)에 위치, 발밑 정렬용 자식 필요
- Pixel Adventure: 스프라이트 한 장 → `flipX`, 단일 바운드, Animator 가 루트에 위치,
  발끝이 칸 바닥에 붙어 있어 정렬 자체가 불필요

교훈은 **애셋의 형태가 Presentation 계층의 구조를 결정한다**는 것입니다. 시뮬레이션은
어느 쪽을 선택하든 한 줄도 바뀌지 않았습니다. 계층을 분리해 둔 것의 이점을 이번에
처음으로 실감했습니다.

### SPUM 을 쓰지 않기로 한 기술적 이유도 있었다

취향의 문제만은 아니었습니다.

- SPUM 스크립트는 asmdef 가 없어 `Assembly-CSharp` 에 들어갑니다. asmdef 로 만든
  어셈블리는 `Assembly-CSharp` 를 참조할 수 없으므로(참조 방향이 반대입니다),
  `Blast.Presentation` 에서 `SPUM_Prefabs` 를 호출하는 코드는 컴파일되지 않습니다
- SPUM 에 asmdef 를 억지로 추가하면 상황이 더 나빠집니다. `SPUM_Prefabs.cs` 가
  **전역 네임스페이스에 `public enum PlayerState`** 를 선언하는데, 이 프로젝트의
  `Blast.Core.PlayerState` 와 이름이 충돌해서 `using Blast.Core;` 가 있는 파일마다
  CS0104 오류가 발생합니다
- SPUM 의 `PlayAnimation()` 은 내부에서 `SetTrigger` 를 사용합니다. 재조정에 대비한
  규칙과 정면으로 충돌합니다

결국 "애셋으로만 쓰고 스크립트는 쓰지 않는다"가 유일하게 성립하는 사용 방법이었는데,
그렇게 하면 SPUM 을 도입해서 얻는 이점의 절반이 사라집니다.

### 스프라이트가 사라진 버그를 파일에서 찾았다

인스펙터에서는 원인이 보이지 않았습니다. Animator 는 Jump 상태에 정상적으로
진입하고 재생 진행도도 올라가는데 캐릭터만 화면에 없었습니다. `.anim` 파일을 열어
`value: {fileID: 0}` 을 확인하고 나서야 "클립이 null 을 대입하고 있다"는 것이
확정되었습니다.

**증상이 여러 갈래로 나타날 때는 파일을 직접 읽는 방법이 가장 빠릅니다.** 이번에는 세
가지 증상(스프라이트가 사라짐, 재생 진행도가 요동침, Fall 상태에 진입하지 않음)이
각각 다른 원인이었고, 파일에서 세 가지를 한 번에 확인할 수 있었습니다.

- 스프라이트가 사라짐 → 클립의 PPtr 커브가 null (슬라이스가 0개인 Multiple 텍스처를 드래그)
- 재생 진행도가 요동침 → 1프레임 클립(0.05초)이 초당 20번 반복. 버그가 아님
- Fall 상태에 진입하지 않음 → 전이 조건의 파라미터가 `VerticalVelocity` 가 아니라
  `Speed`. `Speed` 는 절댓값이라 `< -0.1` 이 영원히 거짓
