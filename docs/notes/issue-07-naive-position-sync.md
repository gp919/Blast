# 이슈 #7: NetworkVariable 기반 나이브 위치 동기화

- 날짜: 2026-08-17
- 브랜치: `feat/7-naive-position-sync`
- 다룬 개념: 어셈블리와 링크, MonoBehaviour 수명주기, 값 타입과 참조 타입,
  방어적 복사, GC, 이벤트, NGO 의 ILPP 와 RPC, 에셋 GUID

구성: 1 코드 리뷰 / 2 Unity 개념 대응 / 3 스크립트 파일별 설명

---

## 1. 코드 리뷰

### 실제 결함: 접속 전에 누른 점프가 유입됨

```csharp
private void Update()
{
    _inputSource.Poll();          // 스페이스를 누르면 _jumpLatched = true

    if (_entries.Count == 0)
    {
        _lastFrameTickCount = 0;
        return;                   // ConsumeEdges 를 안 부르고 나감
    }
```

`KeyboardInputSource` 는 점프를 래치로 붙잡아 둡니다. 프레임과 틱이 1대1이 아니라
필요한 장치인데, **틱이 돌지 않는 구간에서는 래치를 해제할 주체가 없습니다.**

재현: Play 후 접속 전에 스페이스 연타 → Start Host → 캐릭터가 스폰하자마자 점프.

이슈 #7 이 만든 것이 아니라 그 전부터 있었습니다. 예전에는 조건이
`_localPlayer == null` 이었을 뿐 구조는 같습니다.

수정은 한 줄입니다.

```csharp
if (_entries.Count == 0)
{
    _inputSource.ConsumeEdges();   // 시뮬레이션이 안 도는 구간의 에지는 버린다
    _lastFrameTickCount = 0;
    return;
}
```

**같은 함정의 어려운 버전이 3주차에 옵니다.** 재조정은 과거 틱부터 다시 실행하는데,
그 재실행 구간에서 에지 입력을 어떻게 다룰지가 같은 문제입니다.

상태: 미수정. 다음 이슈로 이월.

### 호출되지 않는 안전장치: `PlayerRegistry.Clear()` 호출부 없음

Shutdown 후 재접속에서 잔여 항목을 막으려고 만들었는데 아무도 호출하지 않습니다.
**재접속 경로를 한 번도 테스트하지 않았다는 뜻이기도 합니다.**

Domain Reload 가 켜져 있어 Play 진입마다는 초기화됩니다. 그러나 Play 중에
Shutdown 후 다시 Start Host 를 하면 어떻게 되는지 확인된 바 없습니다.

상태: 미확인. 3주차 예측 작업 전에 확인할 것.

### 사소한 것 셋

- `TickDriver.SimulatedHereCount` 가 프로퍼티인데 O(n) 순회를 한다. 인스펙터가
  매 프레임 읽는다. 2인이라 무해하지만 **필드 읽기처럼 보이는 것이 내부에서 루프를
  도는 것은 적절하지 않다.** 메서드로 두는 편이 낫다
- `RequestAuthorityModeRpc` 가 `GameObject.GetComponent<NetworkPlayer>()` 로 역방향
  조회를 한다. 핸들이 이미 `IPlayerLink` 를 들고 있으므로 거기에 메서드를 두는 편이 직접적이다
- F1, F2, F3 는 **Game 뷰에 포커스가 있어야** 동작한다. IMGUI 키 이벤트라서 그렇다.
  촬영 중 인스펙터를 클릭하면 동작하지 않는다

### 유지할 판단

- `IsSimulatedHere` 하나로 시뮬레이션 루프와 렌더 루프가 같은 결정을 공유한다.
  둘이 어긋날 수 없는 구조다
- `IPlayerLink` 를 `internal` 로 막아 NGO 타입이 어셈블리 밖으로 나가지 않는다
- `NetworkPeer` 가 `IsHost` 를 아예 노출하지 않는다. 서버 조건이 `IsServer` 로만
  써지도록 타입 수준에서 유도한 것이다

---

## 2. Unity 와 C# 을 자체 엔진 개념으로

작업자 배경은 C++/DX11 자체 엔진입니다. 아래는 그 경험에 대응시킨 설명입니다.

### 어셈블리 = 링크 단위

`asmdef` 하나가 DLL 하나입니다. 참조 방향은 링크 의존성이고, 순환 참조는 링커가
거부하듯 Unity 도 거부합니다.

**CS0012 는 불완전 타입 문제와 같습니다.** 전방 선언만 있는 타입의 멤버에 접근하면
C++ 컴파일러가 정의를 요구하듯, 상위 어셈블리가 `NetworkPlayer` 를 변수로 잡으면
컴파일러가 기반 클래스 `NetworkBehaviour` 의 정의를 요구합니다. **API 를 한 줄도
쓰지 않아도** 그렇습니다. 멤버 조회 테이블을 만들려면 상속 체인을 끝까지 봐야
하기 때문입니다.

`PlayerHandle` 이 하는 일이 **PIMPL** 입니다. 구현 타입을 감추고 불투명한 핸들만
노출하는 것. 여기서는 `IPlayerLink` 가 `internal` 이라 DLL 밖에서 보이지 않는 것이
익명 구조체 포인터 역할을 합니다.

`tools/check-layering.ps1` 이 못 잡는 것도 같은 관점입니다. **링크 그래프는 보지만
타입 노출면은 보지 않습니다.** 라이브러리 A 가 B 를 링크하는 것은 정상인데, A 의
공개 헤더가 B 의 타입을 반환하기 시작하면 A 를 쓰는 모두가 B 를 링크해야 합니다.
그것이 CS0012 였습니다.

### MonoBehaviour = 엔진이 소유한 콜백

자체 엔진에서는 `main()` 을 직접 들고 `Engine::Update()` 안에 시스템 호출 순서를
손으로 적었습니다. Unity 는 반대입니다. 엔진이 루프를 소유하고 컴포넌트를 호출합니다.

**핵심은 컴포넌트 간 `Update()` 호출 순서가 명세되지 않았다는 것입니다.**
인스턴스화 순서나 씬 저장 순서에 따라 달라질 수 있습니다. `TickDriver` 가 씬에
하나만 있고 입력 폴링까지 직접 소유하는 이유가 이것입니다. **순서를 엔진에
맡기지 않고 함수 본문에 명시한 것**이며, 자체 엔진에서 하던 방식으로 되돌린
셈입니다.

| Unity | 자체 엔진 |
|---|---|
| `Awake` | 생성자. 다른 오브젝트를 참조하면 안 되는 구간 |
| `OnEnable` / `OnDisable` | 옵저버 등록 / 해제 |
| `Update` | 프레임 틱 |
| `OnDestroy` | 소멸자 |

`OnEnable` 에서 구독하고 `OnDisable` 에서 해제하는 대칭이 RAII 를 모방한 것입니다.
C# 에는 결정적 소멸자가 없어 **직접 짝을 맞춰야 합니다.**

### struct 와 class: 가장 혼동하기 쉬운 지점

C++ 에서 `struct` 와 `class` 는 기본 접근 지정자만 다르고, 값이냐 참조냐는
**사용하는 쪽**이 정합니다 (`T`, `T&`, `T*`). C# 은 **타입 선언에서 고정됩니다.**

- `struct` = 값 타입. 대입하면 복사. 배열에 인라인 저장
- `class` = 참조 타입. 대입하면 포인터 복사. **항상 힙**

`PlayerState` 가 `struct` 인 것은 POD 로 두고 통째로 복사하고 저장하기 위해서입니다.
3주차 스냅샷 링버퍼가 `PlayerState[]` 가 되면 요소가 인라인으로 저장되어 캐시
친화적입니다. `class` 였다면 포인터 배열이 되고 요소마다 힙 객체가 흩어집니다.

`in PlayerState state` 가 `const PlayerState&` 입니다.

**C++ 에 없는 함정이 하나 있습니다.**

```csharp
private FixedTickAccumulator _accumulator;   // readonly 로 바꾸면 깨진다
```

`readonly` struct 필드에 상태를 바꾸는 메서드를 호출하면 C# 컴파일러가 **아무 경고 없이
복사본을 만들어** 거기에 호출합니다. 원본은 바뀌지 않습니다. C++ 이라면 `const`
객체에 비-const 멤버 함수 호출은 컴파일 에러인데, C# 은 조용히 복사합니다.

증상이 까다롭습니다. 60fps 에서는 우연히 맞고 다른 프레임레이트에서만 틀립니다.
Rider 가 readonly 로 만들라고 제안하는데 **따르면 안 됩니다.**

`PlayerSimEntry` 를 `struct` 가 아니라 `class` 로 둔 것도 같은 이유입니다.
`List<T>` 의 인덱서는 값 복사본을 돌려주므로, struct 였다면
`_entries[i].State = ...` 가 복사본만 고치고 사라집니다.

### GC: 프레임 아레나가 없는 환경

`new` 는 힙 할당이고 해제는 GC 가 합니다. 문제는 **언제 하는지 모른다**는 것이고,
수집이 실행되면 프레임이 멈춥니다.

자체 엔진에서 프레임 아레나 할당자를 두고 프레임 끝에 리셋했다면, 여기서는
**애초에 할당하지 않는 것**이 유일한 방어입니다. `GetComponent` 를 스폰 시 한 번만
캐싱하고, `StringBuilder` 를 재사용하고, 델리게이트를 미리 만들어 두는 것이
전부 같은 목적입니다.

C++ 에서 보이지 않던 숨은 할당 넷:

| 숨은 할당 | 왜 |
|---|---|
| 람다에서 지역변수 캡처 | 클로저 객체가 힙에 생긴다 |
| struct 를 `object` 나 인터페이스로 취급 | 박싱. 힙에 상자를 만들어 담는다 |
| 문자열 `+` 연결 | 문자열이 불변이라 매번 새 배열 |
| `List<T>` 를 `foreach` | 경우에 따라 이터레이터가 박싱된다 |

`ConnectionHud` 가 상태 변화 시에만 문자열을 만드는 이유가 이것입니다.
`OnGUI` 는 초당 수백 번 돕니다.

### event = 멀티캐스트 함수 포인터

`event Action<T>` 는 `std::vector<std::function<void(T)>>` 에 가깝습니다.
`+=` 가 push_back, `-=` 가 제거입니다.

**`static` 이벤트는 전역 옵저버 리스트입니다.** `PlayerRegistry.PlayerSpawned` 가
그렇습니다. 구독 해제를 빠뜨리면 이벤트가 강한 참조를 들고 있어 구독자가 GC 에
수집되지 않습니다. C++ 에서 옵저버 등록만 하고 해제하지 않은 것과 같습니다.

`TickDriver` 의 `OnEnable` / `OnDisable` 짝, `ConnectionHud` 의 `Start` /
`OnDestroy` 짝이 그 방어입니다.

### NGO 의 RPC 는 코드 생성의 결과다

`[Rpc]` 가 붙은 메서드는 **컴파일 후 IL 이 재작성됩니다.** ILPP (IL Post Processing)
라고 하며, 빌드 파이프라인이 어셈블리를 열어 메서드 본문 앞에 "호출자가 원격이면
직렬화해서 보내고 리턴, 아니면 원래 본문 실행" 코드를 끼워 넣습니다.

자체 엔진이었다면 IDL 을 쓰고 코드 생성기를 돌리거나 매크로로 스텁을 만들었을
자리입니다. 그래서 제약이 생깁니다. 메서드 이름이 `Rpc` 로 끝나야 하고 파라미터가
직렬화 가능해야 합니다. **생성기가 이해할 수 있는 형태여야 한다**는 뜻입니다.

`NetworkVariable<T>` 는 레플리케이션 프로퍼티입니다. 값이 바뀌면 더티 플래그가
설정되고, 전송 틱(30Hz)에 더티한 것만 모아 보냅니다. 값이 같으면 더티가 서지 않아
정지한 플레이어는 대역폭을 쓰지 않습니다. **자체 엔진에서 직접 구현했을 델타 압축의
가장 단순한 형태입니다.**

### 에셋과 GUID

`.meta` 파일 하나가 에셋 하나의 GUID 를 들고 있습니다. 씬과 프리팹은 **경로가
아니라 GUID 로** 서로를 참조합니다. 그래서 파일을 옮겨도 참조가 깨지지 않고,
**`.meta` 를 삭제하면 새 GUID 가 발급되어 참조가 전부 끊깁니다.**

자체 엔진의 에셋 DB 와 리소스 핸들 그대로입니다. 다른 점은 GUID 목록이 별도 DB 가
아니라 파일마다 흩어져 있고, 그래서 Git 에 같이 커밋해야 한다는 것입니다.

씬 파일(`.unity`)은 **직렬화된 오브젝트 그래프**입니다. 오브젝트와 그 필드값이
통째로 들어 있습니다. `[SerializeField]` 를 붙인 필드가 여기 저장됩니다.
**시뮬레이션 상태에 붙이면 안 되는 이유가 이것**이며, 런타임 값이 레벨 파일에
저장됩니다.

---

## 3. 스크립트 파일별 설명

이 이슈에서 수정한 파일은 8 개입니다. 신규 3, 재작성 3, 부분 수정 2.

```
Assets/Scripts/
  Network/  NetworkPeer.cs          신규   38줄
            NetworkInputCommand.cs  신규   54줄
            IPlayerLink.cs          신규   40줄
            PlayerHandle.cs         재작성 +70
            NetworkPlayer.cs        재작성 +208
  Game/     TickDriver.cs           재작성 +332
            ConnectionHud.cs        수정   +64
  Editor/   TickDriverEditor.cs     수정   +31
```

### 입력 하나가 지나가는 경로

파일을 하나씩 보기 전에 전체 흐름부터 봅니다. 이 경로가 곧 파일 목록입니다.

```
클라이언트                                          서버
----------                                          ----
TickDriver.Update
  KeyboardInputSource.Sample(tick)
      | InputCommand  (Core, NGO 를 모름)
      v
  PlayerHandle.SendInput
      |
      v
  IPlayerLink.SubmitInput           <- internal. 여기가 어셈블리 경계
      |
      v
  NetworkPlayer.SubmitInputRpc
      |
      +--- NetworkInputCommand ---> SubmitInputRpc 본문이 서버에서 실행
           (와이어 표현, 30Hz)          _latestInput 슬롯에 OR 병합 저장
                                              |
                                              v
                                     TickDriver.StepSimulatedHere
                                       TryConsumeInput
                                       SimulationWorld.Step   <- 여기서만 상태가 바뀜
                                              | PlayerState
                                              v
                                     PlayerHandle.PublishState
                                              |
                                              v
  NetworkPlayer.HandlePositionChanged <- NetworkVariable<Vector2> 값 변경 통지
      |                                    (30Hz 전송 틱)
      v
  PlayerHandle.ApplyNetState
      |
      v
  TickDriver.RenderAll
    Render(state, state, 0f)          <- 보간 없음. 3주차가 들어올 자리
```

**한 방향으로만 흐른다는 점**을 보세요. 상위 계층은 아래로 호출하고, 아래에서 위로
올라오는 것은 이벤트뿐입니다. 자체 엔진에서 시스템 간 결합을 끊을 때 쓰던 구조와
같습니다.

### 신규 파일

#### `Network/NetworkPeer.cs`: 역할 조회 창구

`TickDriver` 가 "내가 서버인가"를 알아야 하는데, `NetworkManager.Singleton.IsServer`
를 직접 읽으면 `Blast.Game` 이 NGO 를 참조하게 됩니다. 그 한 줄을 대신 조회해 주는
`static` 클래스입니다.

파일 하나를 따로 둘 만한 일인지 의문이 들 수 있지만, **이 타입의 설계는 무엇을 노출하느냐가 아니라
무엇을 노출하지 않느냐에 있습니다.**

```csharp
public static bool IsServer { get; }
public static bool IsClient { get; }
// IsHost 는 없다
```

`IsHost` 를 넣지 않은 것이 요점입니다. 서버 분기를 `IsHost` 로 쓰기 시작하면 그 코드는
데디케이티드 서버에서 실행되지 않습니다. **타입에 존재하지 않으면 실수할 수 없다**는
방식을 택했습니다. C++ 에서 위험한 생성자를 `explicit` 로 막거나 `delete` 하는 것과
같은 성격입니다.

값을 캐싱하지 않고 매번 조회합니다. 캐싱하면 "누가 언제 갱신하는가"가 생기고,
한 곳이라도 빠뜨리면 역할이 틀린 채로 프레임이 돕니다.

#### `Network/NetworkInputCommand.cs`: 와이어 표현

`Core/InputCommand` 를 그대로 보내려면 거기에 `INetworkSerializable` 을 붙여야 하고,
그러면 **참조 그래프 최하단인 `Blast.Core` 가 NGO 를 참조하게 됩니다.** Core 를 참조하는
Simulation, Input, EditMode 테스트까지 전부 NGO 에 의존하게 됩니다. 네트워크 없이
시뮬레이션만 실행해 보는 경로가 막히고, 그 경로가 막히면 결정성 검증 자체를 할 수 없습니다.

그래서 같은 필드를 가진 별도의 구조체를 Network 계층에 따로 뒀습니다.

```csharp
public static NetworkInputCommand From(in InputCommand command)
public InputCommand ToCommand()
```

**메모리 표현과 와이어 표현의 분리**입니다. 자체 엔진에서 렌더 정점 구조체와 파일에
저장하는 정점 포맷을 따로 두던 것과 같습니다. 지금은 필드가 1대1이라 낭비처럼 보이지만,
비트 패킹이나 델타 압축을 넣을 때 시뮬레이션 구조체를 건드리지 않아도 됩니다.

`NetworkSerialize` 는 읽기와 쓰기를 **한 함수로 겸합니다.**

```csharp
serializer.SerializeValue(ref Tick);
```

`BufferSerializer<T>` 가 읽기 모드인지 쓰기 모드인지에 따라 같은 호출이 반대로 동작합니다.
읽기 코드와 쓰기 코드가 어긋날 수 없게 만드는 관용구이며, C++ 직렬화에서
`template<class Archive> void serialize(Archive&)` 하나로 저장과 로드를 겸하던 것과
정확히 같은 패턴입니다.

**필드 추가 시 순서가 곧 프로토콜입니다.** 한쪽만 필드를 늘리면 그 뒤의 값이 전부 밀려
읽히고, 컴파일은 통과하므로 실행 중에야 드러납니다.

#### `Network/IPlayerLink.cs`: 어셈블리 경계선

`internal` 인터페이스입니다. **이 파일의 존재 이유가 `internal` 키워드 하나에 있습니다.**

`PlayerHandle` 은 상위 계층에 공개되지만, 그 뒤에서 실제 일을 하는 `NetworkPlayer` 는
`NetworkBehaviour` 파생이라 밖으로 나갈 수 없습니다. 핸들이 구현체를 참조하는 통로가
필요한데, 그 통로의 타입이 `public` 이면 다시 어셈블리 밖으로 노출됩니다.

C++ 로 옮기면 이렇습니다.

```cpp
// PlayerHandle.h  (공개 헤더)
class IPlayerLink;              // 전방 선언만
class PlayerHandle { IPlayerLink* _link; };

// IPlayerLink.h  (내부 헤더. 설치하지 않음)
class IPlayerLink { virtual void SubmitInput(...) = 0; };
```

`internal` 이 "설치하지 않는 헤더"에 해당합니다. C# 에는 헤더가 없으므로 접근 지정자가
그 역할을 합니다.

델리게이트(`Action`) 세 개를 개별적으로 연결하지 않고 인터페이스로 묶은 이유는, **셋 중 하나만 null 인
상태를 만들 수 없게 하기 위해서**입니다. 인터페이스는 구현체가 전부 제공하는 것을
컴파일 시점에 강제합니다.

### 재작성한 파일

#### `Network/PlayerHandle.cs`: 식별자에서 상태 운반체로

이슈 #5 에서는 `GameObject`, `OwnerId`, `IsLocalOwner`, `SpawnIndex` 만 담은 값 묶음이었습니다.
이번에 **양방향 통로**가 됐습니다.

| 방향 | 메서드 | 누가 부르나 |
|---|---|---|
| 상행 | `SendInput` | 소유 클라이언트 |
| 상행 | `SubmitState` | 클라 권위의 소유자 |
| 하행 | `TryConsumeInput` | 서버 |
| 하행 | `PublishState` | 서버 |
| 수신 | `NetPosition`, `NetFacing` | 모든 피어의 렌더 |

생성자를 `internal` 로 바꾼 것이 이번 변경에서 중요합니다.

```csharp
internal PlayerHandle(GameObject gameObject, ulong ownerId, ..., IPlayerLink link)
```

`_link` 없이 만들어진 핸들이 레지스트리에 포함되면 `SendInput` 에서 널 참조 예외가
발생합니다. **생성 경로를 NGO 스폰 하나로 고정해** 그 상태 자체를 없앴습니다.

`IsClientAuthority` 를 필드가 아니라 프로퍼티로 매번 조회하는 것도 `NetworkPeer` 와 같은
이유입니다. 권한 모드는 서버가 방송하는 값이라 언제든 바뀔 수 있고, 복사해두면
어긋난 상태로 프레임이 도는 경로가 생깁니다.

`ApplyNetState` 만 `internal` 입니다. **수신 상태를 쓰는 것은 NGO 콜백뿐이어야 하고,
상위 계층은 읽기만 해야 합니다.** 프로퍼티가 `private set` 인 것과 짝을 이룹니다.

#### `Network/NetworkPlayer.cs`: NGO 와 직접 접하는 유일한 지점

프리팹에 붙는 컴포넌트이고, **이 프로젝트에서 NGO API 를 실제로 호출하는 곳은 여기와
`ConnectionLauncher` 뿐입니다.** 하는 일이 셋입니다.

**1. 스폰 시 핸들을 만들어 레지스트리에 등록**

```csharp
public override void OnNetworkSpawn()
{
    _position.OnValueChanged += HandlePositionChanged;   // 구독이 먼저
    _facing.OnValueChanged += HandleFacingChanged;

    _handle = new PlayerHandle(gameObject, OwnerClientId, IsOwner, (int)OwnerClientId, this);
    _handle.ApplyNetState(_position.Value, _facing.Value);
    PlayerRegistry.Register(_handle);
}
```

**순서가 조건입니다.** `Register` 가 동기적으로 `TickDriver.HandlePlayerSpawned` 를 부르고,
서버라면 그 안에서 스폰 상태를 발행합니다. 구독이 뒤에 있으면 그 첫 발행을 놓칩니다.
초기화 순서가 결과를 바꾸는 전형적인 자리이고, 자체 엔진에서 옵저버 등록과 초기 이벤트
발행 순서를 맞추던 것과 같습니다.

`OnNetworkSpawn` 은 `Awake` 나 `Start` 가 아니라 **NGO 가 오브젝트를 네트워크에 편입시킨
시점**입니다. 이 시점에야 `OwnerClientId` 와 `IsOwner` 가 확정됩니다.

`ApplyNetState` 한 줄이 뒤늦게 접속한 피어를 위한 것입니다. 스폰 메시지에 실려온 현재
값을 즉시 반영하지 않으면, 이미 이동해 있는 원격 캐릭터가 다음 값 변경 전까지 스폰
위치에 서 있는 것으로 보입니다.

**2. 입력을 서버로 전달한다**

```csharp
[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
private void SubmitInputRpc(NetworkInputCommand command)
```

기존 `[ServerRpc(RequireOwnership = true)]` 가 두 축으로 분리된 형태입니다.
`SendTo` 는 어디서 실행되는가, `InvokePermission` 은 누가 호출할 수 있는가.
`Owner` 권한이 **서버 권위의 최소 방어선**이고, F2 로 다른 플레이어에게 입력을 보내
`RpcException` 이 나는 것을 확인한 것이 이 줄의 관측 수단입니다.

본문에 이번 이슈의 결함 수정이 들어 있습니다.

```csharp
if (_hasInput) { received.JumpPressed |= _latestInput.JumpPressed; }
```

입력은 60Hz 로 만들어지는데 NGO 전송 틱은 30Hz 라 서버가 한 프레임에 RPC 를 둘씩 받습니다.
단순히 대입하면 뒤에 도착한 것이 앞의 것을 덮어쓰고, 그 사이에 틱이 돌지 않았으면 점프가 소비 전에
사라집니다. 진단 경위는 `docs/ai-collab-log.md` 2026-08-17 항목에 있습니다.

**근본 해결이 아닙니다.** 순서와 시각 정보는 여전히 버리고, 슬롯은 하나입니다.
제대로 하려면 입력을 틱 번호로 정렬해 보관해야 하고 그것이 3주차 입력 버퍼입니다.

**3. 서버 결과를 전 피어에 발행**

```csharp
private readonly NetworkVariable<Vector2> _position = new NetworkVariable<Vector2>(
    Vector2.zero,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server);
```

권한이 타입 인자가 아니라 생성자 인자입니다. **쓰기 권한을 서버로 고정하면 클라이언트가
대입해도 NGO 가 막습니다.** 위치와 방향만 보내는 것은 `PlayerState` 전체를 보내려면
Core 에 NGO 직렬화를 붙여야 하기 때문이고, 그리는 데 실제로 필요한 값도 이 둘뿐입니다.

`_clientAuthority` 의 쓰기 권한도 서버입니다. Owner 로 열면 **클라이언트가 자기 권한을
스스로 올릴 수 있게 됩니다.** 개발용 토글이라도 이 구조는 지켰습니다.

#### `Game/TickDriver.cs`: 역할에 따라 나뉜 틱 루프

가장 많이 바뀌었습니다. 1인용 루프가 **피어 역할에 따라 다르게 도는 루프**가 됐습니다.

변경의 핵심은 함수 하나입니다.

```csharp
private static bool IsSimulatedHere(PlayerSimEntry entry, bool isServer)
{
    return entry.Handle.IsClientAuthority ? entry.Handle.IsLocalOwner : isServer;
}
```

**이 판단이 정확히 하나의 시뮬레이터를 지정한다는 것**이 전부입니다. 둘이 되면 서로
위치를 덮어쓰면서 캐릭터에 떨림이 생기고, 영이 되면 아무도 움직이지 않습니다. 시뮬레이션 루프와
렌더 루프가 이 함수 하나를 공유하므로 둘이 어긋날 수 없습니다.

`PlayerSimEntry` 가 `class` 인 것을 다시 보세요. 2 절의 방어적 복사와 같은 이야기입니다.
`struct` 였다면 `_entries[i].State = ...` 가 `List<T>` 인덱서가 돌려준 복사본만 고치고
사라집니다.

`PreviousState` 필드가 이번에 추가됐습니다. **틱 알파 보간용이고 네트워크와 무관합니다.**
이 둘을 혼동해 필요한 보간까지 지웠던 것이 `ai-collab-log.md` 의 세 번째 항목입니다.

```csharp
if (simulatedHere)
    entry.Presenter.Render(entry.PreviousState, entry.State, alpha);  // 프레임레이트 문제
else
    entry.Presenter.Render(entry.State, entry.State, 0f);             // 3주차 자리
```

관측 프로퍼티가 대폭 늘었습니다. 전부 `[SerializeField]` 가 아니라 읽기 전용 프로퍼티인
이유는 씬 YAML 오염 때문입니다.

| 프로퍼티 | 이 값이 이상하면 |
|---|---|
| `IsServerPeer` | 꺼져 있는데 캐릭터가 움직이면 클라가 몰래 시뮬레이션 중 |
| `SimulatedHereCount` | 두 피어에서 같은 캐릭터를 동시에 세면 덮어쓰기 중 |
| `LastSentInputTick` | 멈춰 있으면 입력 송신이 끊긴 것 |
| `LocalReceivedInputTick` | 서버에서 멈춰 있으면 RPC 가 도달하지 않는 것 |

**증상 하나에 원인 후보가 여럿일 때 어디를 먼저 보는지**를 값으로 구분해 둔 것입니다.

`_accumulator` 위의 경고 주석은 삭제하지 마세요. 2 절의 방어적 복사 함정이고, Rider 가
`readonly` 로 만들라고 계속 제안합니다.

### 부분 수정

#### `Game/ConnectionHud.cs`: 개발용 조작 패널

F2 소유권 테스트와 F3 권한 전환이 추가됐습니다. IMGUI 인 이유는 며칠 뒤 삭제할 UI 에
Canvas 배선과 씬 YAML 오염을 감수할 이유가 없어서입니다.

**F1/F2/F3 는 Game 뷰에 포커스가 있어야 동작합니다.** IMGUI 키 이벤트라서 그렇고,
촬영 중 인스펙터를 클릭하면 반응하지 않습니다. 2 절 리뷰의 사소한 항목입니다.

`RebuildStatusText` 가 상태 변화 시에만 도는 것에 주의하세요. `OnGUI` 는 프레임당 여러 번
호출되므로 거기서 문자열을 조립하면 초당 수백 개의 문자열이 힙에 쌓입니다.

#### `Editor/TickDriverEditor.cs`: 읽기 전용 인스펙터

에디터 전용 어셈블리라 빌드에 포함되지 않습니다. `#if UNITY_EDITOR` 대신 폴더로 구분하는
방식이며, 자체 엔진에서 툴 코드를 별도 프로젝트로 분리하던 것과 같습니다.

`EditorGUI.DisabledScope(true)` 로 감싼 것이 요점입니다. **시뮬레이션 상태를 에디터에서
바꿀 수 있으면 관측이 아니라 개입입니다.**

```csharp
public override bool RequiresConstantRepaint() => Application.isPlaying;
```

인스펙터는 기본적으로 값이 바뀔 때만 다시 그립니다. 프로퍼티는 변경 통지를 보내지 않으므로
명시적으로 매 프레임 갱신을 요청해야 하고, 편집 중에는 갱신할 것이 없어 Play 중으로
제한했습니다.

### 이 이슈가 손대지 않은 파일

전체 지도입니다. 참조 방향은 아래로만 흐릅니다.

| 어셈블리 | 파일 | 역할 |
|---|---|---|
| Core | `InputCommand` | 틱 하나 분량의 입력. Quake 의 usercmd |
| Core | `PlayerState` | 틱 하나가 끝난 시점의 상태 전부 |
| Core | `FixedTickAccumulator` | 프레임 시간을 틱 수로 변환. 벽시계를 안 읽음 |
| Core | `SimulationConstants` | 60Hz, 프레임당 최대 틱 수 |
| Input | `IPlayerInputSource` | 폴링 인터페이스 |
| Input | `KeyboardInputSource` | 점프 에지를 래치로 붙잡아 둠 |
| Simulation | `SimulationWorld` | 한 틱의 월드 전이. 사실상 순수 함수 |
| Simulation | `CharacterController2D` | BoxCast 기반 kinematic 이동 |
| Simulation | `CharacterTuning` | 이동 수치 struct |
| Presentation | `IPlayerPresenter` | 렌더 인터페이스 |
| Presentation | `PlayerPresenter` | 보간 결과를 Transform 에 반영. 기즈모 |
| Network | `ConnectionLauncher` | NGO 접속 시작과 종료 래퍼 |
| Network | `PlayerRegistry` | 스폰된 플레이어 목록. 등록은 아래에서, 구독은 위에서 |
| Game | `CharacterTuningAsset` | ScriptableObject. 인스펙터에서 만지는 데이터 |

`SimulationWorld` 가 위임만 하는 형태로 남아 있는 것은 의도입니다. **여러 플레이어를
정해진 순서로 진행시키는 자리**이고, 밀치기나 충돌이 들어오면 그 순서가 곧 결정성입니다.
지금 `TickDriver` 가 하고 있는 순회를 그때 여기로 내립니다.
