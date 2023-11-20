using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using static BAP.Helpers.BapBasicGameHelper;
using BAP.Types;
using BAP.Helpers;
using Microsoft.Extensions.Logging;
using BAP.ReactionGames.Components;
using System.Collections.Concurrent;
using Microsoft.VisualBasic;

namespace BAP.ReactionGames
{
    internal enum TypeOfButton
    {
        Off = 0,
        Sword = 1,
        FrownyFace = 2,
        SmilyFace = 3,
        Color = 4

    }

    public class ReactionGameEthan : ReactionGameBase
    {
        ConcurrentDictionary<string, (TypeOfButton typeOfButton, DateTime timeToClear)> currentButtonStatus = new();
        IGameProvider GameProvider;
        int swordGameCount = 0;
        ulong[] swordSprite = new ulong[64];
        ulong[] frownyFace = new ulong[64];
        SwordBonusGame? bonusGame { get; set; } = null;
        private const string FrownyFaceSound = "SgLostTheEntireGame.mp3";
        private const string TooManyWrong = "SgLostTheEntireGame.mp3";
        private const string BeatTheBonusRound = "SqBeatTheBonusRound.mp3";
        private const string StartOfBonusRound = "SgStart.mp3";
        DateTime lastSword = DateTime.MinValue;
        internal override ILogger _logger { get; set; }
        CancellationTokenSource timerTokenSource = new();
        PeriodicTimer timer = default!;
        private IServiceProvider Services { get; set; }
        private ISubscriber<InternalSimpleGameUpdates> InternalUpdatePipe { get; set; }
        IDisposable subscriptions = default!;

        public ReactionGameEthan(IGameProvider gameProvider, ILogger<ReactionGameBase> logger, ISubscriber<ButtonPressedMessage> buttonPressed, IBapMessageSender messageSender, IServiceProvider services, ISubscriber<InternalSimpleGameUpdates> internalUpdatePipe) : base(buttonPressed, messageSender)
        {
            Services = services;
            GameProvider = gameProvider;
            InternalUpdatePipe = internalUpdatePipe;
            base.Initialize(minButtons: 2, useIfItWasLitForScoring: false);
            var bag = DisposableBag.CreateBuilder();
            InternalUpdatePipe.Subscribe(async (x) => await UpdateScoreFromInternalMessage(x)).AddTo(bag);
            subscriptions = bag.Build();
            _logger = logger;
            string path = FilePathHelper.GetFullPath<ReactionGameEthan>("Emoji.png");
            SpriteParser spriteParser = new SpriteParser(path);
            frownyFace = spriteParser.GetSprite(4, 5, 24, 20, 16, 2, 9);
            swordSprite = spriteParser.GetSprite(4, 5, 24, 20, 16, 6, 7);
        }

        public async Task UpdateScoreFromInternalMessage(InternalSimpleGameUpdates update)
        {
            base.correctScore += update.CorrectScore;
            base.wrongScore += update.WrongScore;
            if (update.GameEnded)
            {
                base.UnPauseGame();
                lastSword = DateTime.Now;
                bonusGame?.Dispose();
                MsgSender.PlayAudio(FilePathHelper.GetFullPath<ReactionGameEthan>(BeatTheBonusRound));
                InitializeCurrentButtons();
                foreach (var button in buttons)
                {
                    MsgSender.SendImage(button, new ButtonImage());
                }
                await NextCommand();
            }
            MsgSender.SendUpdate("Internal Score Update");
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

        public override async Task<bool> Start(int secondsToRun, bool runNextCommand = true)
        {
            bonusGame = GameProvider.ReturnGameWithoutEnabling<SwordBonusGame>();
            await Task.Delay(100);
            bonusGame.Dispose();
            lastSword = DateTime.MinValue;
            await base.Start(secondsToRun, false);
            InitializeCurrentButtons();
            await NextCommand();
            StartGameFrameTicker();
            return true;
        }

        private async Task StartGameFrameTicker()
        {
            if (timerTokenSource.IsCancellationRequested)
            {
                timerTokenSource = new();

            }
            timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            var timerToken = timerTokenSource.Token;
            while (await timer.WaitForNextTickAsync(timerToken))
            {
                DoWeNeedToClearSwordsOrSmileys();
            };
            _logger.LogError("The Timer Has Stopped");
        }

        private void DoWeNeedToClearSwordsOrSmileys()
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


        public async override Task<bool> NextCommand()
        {
            //await base.NextCommand();
            string nextNodeId = BapBasicGameHelper.GetRandomNodeId(buttons, "", 12);
            Console.WriteLine($"Setting {nextNodeId} to color");
            currentButtonStatus[nextNodeId] = (TypeOfButton.Color, DateTime.MaxValue);
            MsgSender.SendImage(nextNodeId, GenerateNextButton());
            bool sendFace = ShouldWePerformARandomAction(3);
            bool shouldWeShowTheSword = ShouldWePerformARandomAction(10);


            if (shouldWeShowTheSword && lastSword.AddSeconds(8) <= DateTime.Now && swordGameCount < 5 && !currentButtonStatus.Where(t => t.Value.typeOfButton == TypeOfButton.Sword).Any())
            {
                swordGameCount++;
                Console.WriteLine("Sword Game Count is " + swordGameCount);
                string swordNodeId = GetRandomNodeId(buttons, nextNodeId, 0);
                currentButtonStatus[swordNodeId] = (TypeOfButton.Sword, DateTime.Now.AddSeconds(1));
                Console.WriteLine($"Setting {swordNodeId} to Sword");
                MsgSender.SendImage(swordNodeId, new ButtonImage(swordSprite));
            }
            else if (sendFace)
            {
                bool sendFrownyFace = ShouldWePerformARandomAction(3);
                string faceNodeId = GetRandomNodeId(buttons, nextNodeId, 0);
                Console.WriteLine($"Sentting {faceNodeId} to a face");
                currentButtonStatus[faceNodeId] = sendFrownyFace ? (TypeOfButton.FrownyFace, DateTime.Now.AddSeconds(2)) : (TypeOfButton.SmilyFace, DateTime.Now.AddSeconds(5));
                if (sendFrownyFace)
                {
                    Console.WriteLine("Sending Frowny Face");
                    MsgSender.SendImage(faceNodeId, new ButtonImage(frownyFace));

                }
                else
                {
                    Console.WriteLine("Sending Smily Face");
                    MsgSender.SendImage(faceNodeId, new ButtonImage(PatternHelper.GetBytesForPattern(Patterns.PlainSmilyFace), new BapColor(0, 255, 0)));
                }
            }

            return true;

        }
        public override void Dispose()
        {
            subscriptions.Dispose();
            base.Dispose();
        }

        public bool IsBonusGameRunning
        {
            get
            {
                return bonusGame?.IsGameRunning ?? false;
            }
        }

        public void ForceEndBonusGame()
        {

            bonusGame?.Dispose();

        }
        public async override Task OnButtonPressed(ButtonPressedMessage e)
        {
            if (!GamePaused)
            {
                TypeOfButton currentTypeOfButton = currentButtonStatus[e.NodeId].typeOfButton;
                Console.WriteLine("Current Type of Button is " + currentTypeOfButton);
                Console.WriteLine($"Clearing {e.NodeId} because it was pressed");
                currentButtonStatus[e.NodeId] = (TypeOfButton.Off, DateTime.MaxValue);
                if (currentTypeOfButton == TypeOfButton.Sword)
                {

                    if (bonusGame != null)
                    {
                        bonusGame.Dispose();
                    }
                    bonusGame = GameProvider.ReturnGameWithoutEnabling<SwordBonusGame>();
                    PauseGame(true);
                    MsgSender.PlayAudio(FilePathHelper.GetFullPath<ReactionGameEthan>(StartOfBonusRound));
                    await bonusGame.Start(10);
                }
                else if (currentTypeOfButton == TypeOfButton.FrownyFace)
                {
                    await EndGame("It was a frowny Face", true);
                    MsgSender.PlayAudio(FilePathHelper.GetFullPath<ReactionGameEthan>(FrownyFaceSound));
                    //This use to use TimeSinceLightTurnedOff


                }
                else if (currentTypeOfButton == TypeOfButton.SmilyFace)
                {
                    await RightButtonPressed(e.ButtonPress, false, true, 3);
                }
                else if (currentTypeOfButton == TypeOfButton.Color)
                {
                    await RightButtonPressed(e.ButtonPress, false, true);
                    await NextCommand();
                }
                else
                {
                    await base.WrongButtonPressed(e.ButtonPress);
                }

            }

        }


        public override async Task<bool> EndGame(string message, bool isFailure = false)
        {
            timerTokenSource.Cancel();
            bonusGame?.Dispose();
            return await base.EndGame("Game Ended by User", isFailure);
        }

        public override async Task<bool> WrongButtonPressed(ButtonPress bp, bool runNextCommand = false, bool updateScore = true, int amountToAdd = 1)
        {
            wrongScore++;
            base.PlayNextWrongSound();
            if (wrongScore > 5)
            {
                MsgSender.PlayAudio(FilePathHelper.GetFullPath<ReactionGameEthan>(TooManyWrong));
                await EndGame("Too Many Wrong");
                return false;
            }
            return true;

        }

        public override ButtonImage GenerateNextButton()
        {
            BapColor bapColor = StandardColorPalettes.Default[GetRandomInt(0, StandardColorPalettes.Default.Count - 1)];
            
            return new ButtonImage(PatternHelper.GetBytesForPattern(Patterns.AllOneColor), bapColor);
        }
    }

}
