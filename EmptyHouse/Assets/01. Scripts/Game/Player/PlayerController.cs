using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem; // ★ [추가] 신형 인풋 시스템 네임스페이스

public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;

    [SerializeField] private INputRaeder inputReader;

    private void OnEnable()
    {
        inputReader.MoveAction += Move;
    }
    
    private void OnDisable()
    {
        inputReader.MoveAction -= Move;
    }

    private void Update()
    {
        if (!IsOwner) return;

        float horizontal = 0f;
        float vertical = 0f;

        // ★ 신형 인풋 시스템 기기(키보드)에서 현재 프레임의 입력값을 직접 추출
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) vertical = 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) vertical = -1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) horizontal = -1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) horizontal = 1f;
        }

        Vector3 moveDirection = new Vector3(horizontal, 0f, vertical).normalized;

        if (moveDirection.magnitude >= 0.1f)
        {
            MoveServerRpc(moveDirection);
        }
    }

    [ServerRpc]
    private void MoveServerRpc(Vector3 direction)
    {
        transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);
    }
}