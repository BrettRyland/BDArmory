using System.Collections;
using UnityEngine;
using BDArmory.Core;
using KSP.Localization;
using BDArmory.Competition;
using BDArmory.UI;

namespace BDArmory.Evolution
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class EvolutionWindow : MonoBehaviour
    {
        public static EvolutionWindow Instance;
        private BDAModuleEvolution evolution;

        private int _guiCheckIndex;
        private static readonly float _buttonSize = 20;
        private static readonly float _margin = 5;
        private static readonly float _lineHeight = _buttonSize;
        private readonly float _titleHeight = 30;
        private float _windowHeight; //auto adjusting
        private float _windowWidth;
        public bool ready = false;
        private bool showWindow = true;
        private EvolutionStatus status;

        private void Awake()
        {
            Debug.Log("EvolutionWindow awake");
            if (Instance)
                Destroy(this);
            Instance = this;
        }

        private void Start()
        {
            Debug.Log("EvolutionWindow start");            
        }

        private void Update()
        {
            if (!ready) return;
            status = evolution == null ? EvolutionStatus.Idle : evolution.Status();
        }

        private void OnGUI()
        {
            if (!(showWindow && ready && BDArmorySettings.EVOLUTION_ENABLED))
                return;

            SetNewHeight(_windowHeight);
            BDArmorySetup.WindowRectEvolution = new Rect(
                BDArmorySetup.WindowRectEvolution.x,
                BDArmorySetup.WindowRectEvolution.y,
                BDArmorySettings.EVOLUTION_WINDOW_WIDTH,
                _windowHeight
            );
            BDArmorySetup.WindowRectEvolution = GUI.Window(
                80086,
                BDArmorySetup.WindowRectEvolution,
                WindowEvolution,
                Localizer.Format("#LOC_BDArmory_BDAEvolution_Title"),//"BDA Evolution"
                BDArmorySetup.BDGuiSkin.window
            );
            Misc.Misc.UpdateGUIRect(BDArmorySetup.WindowRectEvolution, _guiCheckIndex);
        }

        private void SetNewHeight(float windowHeight)
        {
            BDArmorySetup.WindowRectEvolution.height = windowHeight;
        }

        public void ShowWindow()
        {
            if (!ready)
                StartCoroutine(WaitForSetup());
            showWindow = true;
        }

        private IEnumerator WaitForSetup()
        {
            while (BDArmorySetup.Instance == null || BDAModuleEvolution.Instance == null)
            {
                yield return null;
            }
            evolution = BDAModuleEvolution.Instance;

            Debug.Log("EvolutionWindow ready");
            ready = true;
            _guiCheckIndex = Misc.Misc.RegisterGUIRect(new Rect());
        }

        private void WindowEvolution(int id)
        {
            GUI.DragWindow(new Rect(0, 0, BDArmorySettings.EVOLUTION_WINDOW_WIDTH - _titleHeight / 2 - 2, _titleHeight));
            if (GUI.Button(new Rect(BDArmorySettings.EVOLUTION_WINDOW_WIDTH- _titleHeight / 2 - 2, 2, _titleHeight / 2, _titleHeight / 2), "X", BDArmorySetup.BDGuiSkin.button))
            {
                showWindow = false;
            }

            float offset = _titleHeight + _margin;
            float width = BDArmorySettings.EVOLUTION_WINDOW_WIDTH;
            float fifth = width / 5.0f;
            GUI.Label(new Rect(_margin, offset, 2 * fifth, _titleHeight), "Evolution: ");
            GUI.Label(new Rect(_margin + 2 * fifth, offset, 3 * fifth, _titleHeight), evolution.GetId());
            offset += _titleHeight;
            GUI.Label(new Rect(_margin, offset, 2 * fifth, _titleHeight), "Status: ");
            string statusLine;
            switch (evolution.Status())
            {
                default:
                    statusLine = status.ToString();
                    break;
            }
            GUI.Label(new Rect(_margin + 2 * fifth, offset, 3 * fifth, _titleHeight), statusLine);
            offset += _titleHeight;
            GUI.Label(new Rect(_margin, offset, 2 * fifth, _titleHeight), "Group: ");
            GUI.Label(new Rect(_margin + 2 * fifth, offset, 3 * fifth, _titleHeight), evolution.GetGroupId().ToString());
            offset += _titleHeight;
            string buttonText;
            bool nextButton = false;
            switch (status)
            {
                case EvolutionStatus.Idle:
                    buttonText = "Start";
                    nextButton = true;
                    break;
                default:
                    buttonText = "Cancel";
                    break;
            }
            if (GUI.Button(new Rect(_margin, offset, nextButton ? 2 * width / 3 - _margin : width - 2 * _margin, _titleHeight), buttonText, BDArmorySetup.BDGuiSkin.button))
            {
                Debug.Log("EvolutionWindow buttonClicked");
                switch (status)
                {
                    case EvolutionStatus.Idle:
                        evolution.StartEvolution();
                        break;
                    default:
                        evolution.StopEvolution();
                        break;
                }
            }
            offset += _titleHeight + _margin;

            _windowHeight = offset;

            BDGUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectEvolution); // Prevent it from going off the screen edges.
        }
    }
}
