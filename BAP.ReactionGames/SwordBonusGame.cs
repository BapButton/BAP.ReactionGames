using MessagePipe;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BAP.Types;
using BAP.Helpers;
using System.Collections.Concurrent;

namespace BAP.ReactionGames
{
    public enum SBGCustomImages
    {
        Sword = 0,
        Crown = 1
    }

    public class SwordBonusGame : ReactionGameBase
    {
        CancellationTokenSource timerTokenSource = new();
        PeriodicTimer timer = default!;
        ConcurrentDictionary<string, (TypeOfButton typeOfButton, DateTime timeToClear)> currentButtonStatus = new();
        internal override ILogger _logger { get; set; }
        private const string LostBonusRound = "SgLost.wav";
        private const string LostEntireGame = "SgLostTheEntireGame.wav";

        private const string StartOfEntireGame = "SgStartOfEntireGame.mp3";
        private const string WonEntireGame = "SgWonTheEntireGame.wav";
        private const string BeatTheBonusRound = "SqBeatTheBonusRound.wav";
        private const string HitTheCrown = "SqHitTheCrown.mp3";
        private ulong[] SwordSprite { get; set; } = new ulong[64];
        private ulong[] CrownSprite { get; set; } = new ulong[64];
        private int NumberOfTicksPerImage { get; set; } = 4;
        private int CrownDelay { get; set; } = 500;
        private int LengthOfTickInMS => 250;
        private int NumberOfTicks { get; set; } = 0;


        private IPublisher<InternalSimpleGameUpdates> InternalGamePipe { get; set; } = default!;
        private string CrownNodeId;
        public SwordBonusGame(ILogger<SwordBonusGame> logger, IPublisher<InternalSimpleGameUpdates> internalGameUpdates, ISubscriber<ButtonPressedMessage> buttonPressed, IBapMessageSender messageSender) : base(buttonPressed, messageSender)
        {
            InternalGamePipe = internalGameUpdates;
            CrownNodeId = "";
            _logger = logger;

            string path = FilePathHelper.GetFullPath<SwordBonusGame>("SwordBonusGame.bmp");
            SpriteParser spriteParser = new SpriteParser(path);
            var sprites = spriteParser.GetCustomImagesFromCustomSprite();
            SwordSprite = sprites[0];
            CrownSprite = sprites[1];
            base.Initialize(minButtons: 2, useIfItWasLitForScoring: true);

        }


        private async Task StartGameFrameTicker()
        {
            if (timerTokenSource == null || timerTokenSource.IsCancellationRequested)
            {
                timerTokenSource = new();

            }
            timer = new PeriodicTimer(TimeSpan.FromMilliseconds(LengthOfTickInMS));
            var timerToken = timerTokenSource.Token;
            while (await timer.WaitForNextTickAsync(timerToken))
            {
                if (NumberOfTicks % NumberOfTicksPerImage == 0)
                {
                    await NextCommand();
                }
                DoWeNeedToClearAnyCrowns();
                NumberOfTicks++;
            };
            _logger.LogError("The Timer Has Stopped");
        }


        private void DoWeNeedToClearAnyCrowns()
        {
            for (int i = 0; i < currentButtonStatus.Count; i++)
            {
                string button = buttons[i];
                if (currentButtonStatus[button].timeToClear <= DateTime.Now)
                {
                    Console.WriteLine($"Clearing {button} because the time has expired");
                    currentButtonStatus[button] = (TypeOfButton.Off, DateTime.MaxValue);
                    MsgSender.SendImage(button, new ButtonImage());

                }
            }
        }

        private void InitializeCurrentButtons()
        {
            Console.WriteLine("Clearing the button status");
            currentButtonStatus = new();
            foreach (var button in buttons)
            {
                currentButtonStatus.TryAdd(button, (TypeOfButton.Off, DateTime.MaxValue));
            }
        }

        public override ButtonImage GenerateNextButton()
        {
            return new ButtonImage(PatternHelper.GetBytesForPattern(Patterns.AllOneColor), new BapColor(255, 120, 120));
        }



        private void BonusGameEnded()
        {
            InternalGamePipe.Publish(new InternalSimpleGameUpdates(0, 0, true, ""));
        }
        public override async Task<bool> RightButtonPressed(ButtonPress bp, bool runNextCommand = true, bool updateScore = true, int amountToAdd = 1)
        {
            await base.RightButtonPressed(bp, true, false);
            InternalGamePipe.Publish(new InternalSimpleGameUpdates(2, 0, false, ""));
            return true;
        }
        public override async Task<bool> EndGame(string message, bool isFailure = false)
        {
            _logger.LogInformation(message);
            base.gameTimer.Dispose();

            IsGameRunning = false;
            InternalGamePipe.Publish(new InternalSimpleGameUpdates(0, 0, true, ""));
            return true;
        }
        public override async Task<bool> WrongButtonPressed(ButtonPress bp, bool runNextCommand = false, bool updateScore = true, int amountToAdd = 1)
        {
            //There is no wrong button scoring in the Bonus round. You just go fase;
            //InternalGamePipe.Publish(new InternalSimpleGameUpdates(0, 1, false));
            base.PlayNextWrongSound();
            //PlaySound(LostBonusRound);
            //await EndGame("Wrong button pressed. Ending the bonus round");
            return false;
        }
        public override async Task<bool> NextCommand()
        {
            if (IsGameRunning)
            {
                if (BapBasicGameHelper.ShouldWePerformARandomAction(6))
                {
                    CrownNodeId = BapBasicGameHelper.GetRandomNodeId(buttons, lastNodeId, 2);
                    currentButtonStatus[CrownNodeId] = (TypeOfButton.Sword, DateTime.Now.AddMilliseconds(CrownDelay));
                    MsgSender.SendImage(CrownNodeId, new(CrownSprite));
                }
                string nextNodeId = BapBasicGameHelper.GetRandomNodeId(buttons, "", 4);
                currentButtonStatus[nextNodeId] = (TypeOfButton.Color, DateTime.Now.AddMilliseconds(NumberOfTicksPerImage * LengthOfTickInMS));
                MsgSender.SendImage(nextNodeId, GenerateNextButton());
            }

            return true;
        }

        public override Task<bool> CommandSent()
        {
            if (lastNodeId == CrownNodeId)
            {
                CrownNodeId = "";
            }
            return Task.FromResult(true);
        }
        public async override Task OnButtonPressed(ButtonPressedMessage e)
        {
            if (IsGameRunning)
            {
                TypeOfButton currentTypeOfButton = currentButtonStatus[e.NodeId].typeOfButton;
                Console.WriteLine("Current Type of Button is " + currentTypeOfButton);
                Console.WriteLine($"Clearing {e.NodeId} because it was pressed");
                currentButtonStatus[e.NodeId] = (TypeOfButton.Off, DateTime.MaxValue);
                if (currentTypeOfButton == TypeOfButton.Sword)
                {

                    await RightButtonPressed(e.ButtonPress, false, true, 20);
                }
                else if (currentTypeOfButton == TypeOfButton.Color)
                {
                    await RightButtonPressed(e.ButtonPress, false, true);
                    await NextCommand();
                }
                else
                {
                    await NextCommand();
                }

            }

        }


        public override async Task<bool> Start(int secondsToRun, bool runNextCommand = true)
        {
            await base.Start(secondsToRun, false);
            InitializeCurrentButtons();
            StartGameFrameTicker();
            NumberOfTicks = 0;
            MsgSender.PlayAudio(FilePathHelper.GetFullPath<SwordBonusGame>(StartOfEntireGame));
            await NextCommand();
            return true;
        }

        async void buttonPressCompleted(object? sender, GameEventMessage e)
        {
            //if (correctButtonsPressed >= 20)
            //{
            //    await EndGame("Reached 20 correct presses");
            //}
        }



        public virtual async void NextButtonevent(Object source, System.Timers.ElapsedEventArgs e)
        {
            await NextCommand();
        }

        public override void Dispose()
        {
            if (timerTokenSource != null && timerTokenSource.IsCancellationRequested == false)
            {
                timerTokenSource.Cancel();
                timerTokenSource.Dispose();
            }

            base.Dispose();
        }
    }
}
