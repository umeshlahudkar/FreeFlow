using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

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

	public bool CanInput(float delay = 0.25f, bool disableOnAvailable = true)
	{
		bool status = isTouchAvailable;
		if (status && disableOnAvailable)
		{
			DisableTouch();

			if (enableCoroutine != null)
			{
				StopCoroutine(enableCoroutine);
			}
			enableCoroutine = StartCoroutine(EnableTouchAfterDelay(delay));
		}
		return status;
	}

	public void DisableTouch()
	{
		if (eventSystem == null) { SetEventSystem(); }

		if (enableCoroutine != null)
		{
			StopCoroutine(enableCoroutine);
		}

		isTouchAvailable = false;
		eventSystem.enabled = false;
	}

	public void DisableTouchForDelay(float delay = 0.25f)
	{
		DisableTouch();

		if (enableCoroutine != null)
		{
			StopCoroutine(enableCoroutine);
		}
		enableCoroutine = StartCoroutine(EnableTouchAfterDelay(delay));
	}

	public void EnableTouch()
	{
		if (eventSystem == null) { SetEventSystem(); }

		isTouchAvailable = true;
		eventSystem.enabled = true;
	}

	public IEnumerator EnableTouchAfterDelay(float delay)
	{
		yield return new WaitForSeconds(delay);
		EnableTouch();
	}
}
