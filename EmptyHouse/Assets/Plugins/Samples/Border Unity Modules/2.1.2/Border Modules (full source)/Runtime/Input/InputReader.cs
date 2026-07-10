using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Border.Input
{
    [CreateAssetMenu(fileName = "InputReader", menuName = "Game/Input Reader")]
    public class InputReader : ScriptableObject, GameInput.IGameplayActions
    {
        public UnityAction<Vector2> MoveAction = delegate { };

        private GameInput _gameInput;

        private void OnEnable()
        {
            if (_gameInput == null)
            {
                _gameInput = new GameInput();

                // _gameInput.##.SetCallbacks(this);
                // _gameInput.##.Enable();
            }
        }

        private void OnDisable()
        {
            DisableAllInput();
        }

        public void DisableAllInput()
        {
            // _gameInput.##.Disable();
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        public void OnLookGamepad(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        public void OnAttack(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        public void OnJump(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        public void OnPause(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        public void OnWSAction(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        public void OnQEAction(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        public void OnFAction(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }
    }
}
