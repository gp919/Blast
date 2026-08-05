using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Player : MonoBehaviour
{
    // 접지로 인정할 최소 법선 y. 벽면을 바닥으로 오인하지 않도록 사용
    private const float MinGroundNormalY = 0.5f;

    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private float _jumpForce = 12f;
    // 콜라이더를 아래로 내보낼 탐지 거리. 너무 크면 공중에서도 접지로 판정됨
    [SerializeField] private float _groundProbeDistance = 0.05f;
    // 비워두면 레이어 필터 없이 자기 자신을 제외한 모든 콜라이더를 바닥으로 취급
    [SerializeField] private LayerMask _groundLayer;

    private Rigidbody2D _rb;
    private SpriteRenderer _spriteRenderer;
    private ContactFilter2D _groundFilter;
    // 매 프레임 GC 할당을 피하기 위한 사전 할당 버퍼
    private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];

    private float _moveInput;
    private bool _isGrounded;

    public bool IsGrounded => _isGrounded;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        // 충돌 시 토크로 인한 회전을 막기 위해 Z축 회전 고정
        _rb.freezeRotation = true;

        _groundFilter = new ContactFilter2D();
        _groundFilter.useTriggers = false;
        // 레이어를 지정하지 않은 상태에서도 점프가 동작하도록, 마스크가 비어 있으면 필터를 걸지 않음
        if (_groundLayer.value != 0)
        {
            _groundFilter.SetLayerMask(_groundLayer);
        }
    }

    private void Update()
    {
        // 계층: Input
        Keyboard keyboard = Keyboard.current;
        _moveInput = 0f;
        bool jumpPressed = false;
        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.isPressed) _moveInput -= 1f;
            if (keyboard.rightArrowKey.isPressed) _moveInput += 1f;
            jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
        }

        // 계층: Presentation
        if (_moveInput != 0f)
        {
            _spriteRenderer.flipX = _moveInput < 0f;
        }

        // 계층: Simulation
        // wasPressedThisFrame은 프레임 단위 이벤트이므로 FixedUpdate로 넘기면 유실될 수 있음
        _isGrounded = CheckGrounded();
        if (jumpPressed && _isGrounded)
        {
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, _jumpForce);
        }
    }

    private void FixedUpdate()
    {
        // 계층: Simulation
        _rb.linearVelocity = new Vector2(_moveInput * _moveSpeed, _rb.linearVelocity.y);
    }

    // 계층: Simulation
    // Rigidbody2D.Cast는 이 바디에 붙은 콜라이더를 결과에서 자동 제외한다.
    // 발밑 탐지가 플레이어 자신의 콜라이더를 잡아 항상 접지로 판정되는 문제를 원천 차단
    private bool CheckGrounded()
    {
        int hitCount = _rb.Cast(Vector2.down, _groundFilter, _groundHits, _groundProbeDistance);
        for (int i = 0; i < hitCount; i++)
        {
            if (_groundHits[i].normal.y >= MinGroundNormalY)
            {
                return true;
            }
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;

        // 캐스트가 끝나는 지점의 콜라이더 위치를 표시
        Bounds bounds = col.bounds;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(
            new Vector3(bounds.center.x, bounds.center.y - _groundProbeDistance, bounds.center.z),
            bounds.size);
    }
}
