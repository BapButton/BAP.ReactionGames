using BAP.UIHelpers.Components;
using Microsoft.AspNetCore.Components;

namespace BAP.ReactionGames.Components
{
    [GamePage("Quickest to 100", "How quickly can you get to 100. The faster you click the better.", "2833fac2-7a69-4216-be2e-2c96f445cc0e")]
    public partial class QuickestToAHundred : GamePage
    {
        [Inject]
        IGameProvider GameHandler { get; set; } = default!;
        private QuickestToAHundredGame game { get; set; } = default!;

        internal TimeSpan TimePlayed
        {
            get
            {
                return game.GameLength;
            }
        }
        private void ShowHighScores(Score? newScore = null)
        {
            if (!game.IsGameRunning)
            {
                int buttonCount = game.buttons.Count;
                if (buttonCount == 0)
                {
                    buttonCount = game.MsgSender.ButtonCount;
                }
                var (shortVersion, longVersion) = game.GetFullDifficulty(buttonCount);
                DialogOptions dialogOptions = new()
                {
                    CloseButton = false,
                    DisableBackdropClick = newScore != null
                };
                DialogParameters dialogParameters = new()
                {
                    { "NewScore", newScore },
                    { "GameDataSaver", game?.DbSaver },
                    { "Description", newScore?.DifficultyDescription ?? longVersion },
                    { "Difficulty", newScore?.DifficultyDescription ?? shortVersion }
                };
                DialogService.Show<HighScoreTable>("High Scores", dialogParameters, dialogOptions);
            }

        }



        public async override Task<bool> GameUpdateAsync(GameEventMessage e)
        {
            if (e.GameEnded)
            {
                StopUpdatingTime();

                await InvokeAsync(() =>
                {
                    if (e.HighScoreAchieved)
                    {
                        Score highScore = game.GenerateScoreWithCurrentData();
                        ShowHighScores(highScore);
                    }

                    StateHasChanged();
                });
            }
            else
            {
                await base.GameUpdateAsync(e);
            }
            return true;
        }
        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            game = (QuickestToAHundredGame)GameHandler.UpdateToNewGameType(typeof(QuickestToAHundredGame));
            if (!game.IsGameRunning)
            {
                game.Initialize();
            }
        }
        public async void EndGame()
        {
            StopUpdatingTime();
            await game.EndSpeedupGame("Closed by player", true);

        }

        public async void StartGame()
        {
            if (!game.IsGameRunning)
            {
                _ = KeepTimeUpdated();
                await game.Start();
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
