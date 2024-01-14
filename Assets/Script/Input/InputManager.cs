using FreeFlow.Util;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FreeFlow.Input
{
	/// <summary>
	/// This class manages input availability by toggling the EventSystem component
	/// and provides methods to control input during specified delays.
	/// </summary>
	public class InputManager : Singleton<InputManager>
	{
		bool isTouchAvailable = true;
		private EventSystem eventSystem;
		private Coroutine enableCoroutine;

		private void Awake()
		{
			SetEventSystem();
		}

		private void SetEventSystem()
		{
			eventSystem = FindObjectOfType<EventSystem>();
		}

		/// <summary>
		/// Checks if touch input is available and optionally disables it for a specified delay.
		/// </summary>
		/// <param name="delay">The delay before enabling touch input again.</param>
		/// <param name="disableOnAvailable">Whether to disable touch input if available.</param>
		/// <returns>True if touch input is available, false otherwise.</returns>
		public bool CanInput(float delay = 0.25f, bool disableOnAvailable = true)
		{
			bool status = isTouchAvailable;
			if (status && disableOnAvailable)
			{
				DisableInput();

				if (enableCoroutine != null)
				{
					StopCoroutine(enableCoroutine);
				}
				enableCoroutine = StartCoroutine(EnableInputAfterDelay(delay));
			}
			return status;
		}

		/// <summary>
		/// Disable Input
		/// </summary>
		public void DisableInput()
		{
			if (eventSystem == null) { SetEventSystem(); }

			if (enableCoroutine != null)
			{
				StopCoroutine(enableCoroutine);
			}

			isTouchAvailable = false;
			eventSystem.enabled = false;
		}

		/// <summary>
		/// Disables Input for a specified time
		/// </summary>
		/// <param name="delay">The delay before enabling input again</param>
		public void DisableInputForDelay(float delay = 0.25f)
		{
			DisableInput();

			if (enableCoroutine != null)
			{
				StopCoroutine(enableCoroutine);
			}
			enableCoroutine = StartCoroutine(EnableInputAfterDelay(delay));
		}


		/// <summary>
		/// Enables Input
		/// </summary>
		public void EnableInput()
		{
			if (eventSystem == null) { SetEventSystem(); }

			isTouchAvailable = true;
			eventSystem.enabled = true;
		}

		/// <summary>
		/// Enable input after a specified delay.
		/// </summary>
		/// <param name="delay">The delay in seconds before enabling input.</param>
		public IEnumerator EnableInputAfterDelay(float delay)
		{
			yield return new WaitForSeconds(delay);
			EnableInput();
		}
	}
}
