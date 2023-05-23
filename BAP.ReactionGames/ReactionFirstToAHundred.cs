using BAP.Types;
using BAP.Helpers;
using BAP.ReactionGames.Components;

namespace BAP.ReactionGames
{

	public class QuickestToAHundredGame : ReactionGameBase
	{
		internal override ILogger _logger { get; set; }
		internal BapColor colorToUse { get; set; }
		//I think I have thread safety issues here. 
		HashSet<string> NodeIdsCurrentlyShowingSomething { get; set; } = new();
		internal int IncorrectPress { get; set; }
		System.Timers.Timer ButtonTimer { get; set; }
		public DateTime GameStartedAt { get; set; }
		public DateTime GameEndedAt { get; set; }
		public int goalScore { get; set; } = 100;
		public IGameDataSaver<QuickestToAHundredGame> DbSaver { get; set; }
		public TimeSpan GameLength
		{
			get
			{
				if (GameStartedAt == DateTime.MinValue)
				{
					return TimeSpan.FromSeconds(0);
				}
				if (IsGameRunning)
				{
					return DateTime.Now - GameStartedAt;
				}

				if (GameEndedAt <= GameStartedAt)
				{
					return DateTime.Now - GameStartedAt;
				}
				return GameEndedAt - GameStartedAt;
			}
		}

		public string GameLengthDisplay
		{
			get
			{
				return GameLength.ToString("mm\\:ss");
			}
		}

		public QuickestToAHundredGame(IGameDataSaver<QuickestToAHundredGame> dbSaver, ISubscriber<ButtonPressedMessage> buttonPressed, ILogger<ReactionGame> logger, IBapMessageSender messageSender) : base(buttonPressed, messageSender)
		{
			_logger = logger;
			colorToUse = StandardColorPalettes.Default[1];
			DbSaver = dbSaver;
			ButtonTimer = new System.Timers.Timer(2000);
			base.Initialize(minButtons: 3);
		}

		public override async Task<bool> Start()
		{
			base.correctScore = 0;
			base.wrongScore = 0;
			GameStartedAt = DateTime.Now;
			GameEndedAt = DateTime.MaxValue;
			
			// Hook up the Elapsed event for the timer. 
			ButtonTimer.Elapsed += TimeForNextButton;
			ButtonTimer.AutoReset = true;
			ButtonTimer.Enabled = true;
			NodeIdsCurrentlyShowingSomething = new();
			return await base.Start(0);
		}

		public async void TimeForNextButton(Object source, System.Timers.ElapsedEventArgs e)
		{
			await SendOutANewImage();
		}

		private async Task SendOutANewImage()
		{
			if (IsGameRunning)
			{
				if (correctScore >= goalScore)
				{
					await EndSpeedupGame("You won!", false);
				}
				string nextNodeId = BapBasicGameHelper.GetRandomItemFromList(buttons.Except(NodeIdsCurrentlyShowingSomething).ToList());
				NodeIdsCurrentlyShowingSomething.Add(nextNodeId);
				if (buttons.Count == NodeIdsCurrentlyShowingSomething.Count)
				{
					GameEndedAt = DateTime.Now;
					IsGameRunning = false;
					MsgSender.SendImage(nextNodeId, GenerateNextButton());
					await Task.Delay(300);
					await EndSpeedupGame("All of the nodes are showing something", true);
				}
				else
				{
					MsgSender.SendImage(nextNodeId, GenerateNextButton());
				}

			}
		}

		public async Task<bool> EndSpeedupGame(string message, bool isFailure = false)
		{
			
			if (IsGameRunning)
			{
				IsGameRunning = false;
				GameEndedAt = DateTime.Now;
				ButtonTimer.Stop();
				return await EndGame(message, isFailure);
			}
			return true;
		}

		public override async Task<bool> NextCommand()
		{
			if (NodeIdsCurrentlyShowingSomething.Count < 4)
			{
				await SendOutANewImage();
			}

			return true;
		}


		public override async Task OnButtonPressed(ButtonPressedMessage e)
		{
			if (IsGameRunning)
			{
				if (NodeIdsCurrentlyShowingSomething.Contains(e.NodeId))
				{
					MsgSender.SendImage(e.NodeId, new ButtonImage());
					NodeIdsCurrentlyShowingSomething.Remove(e.NodeId);
					await RightButtonPressed(e.ButtonPress, true, true, 1);
					if (correctScore >= goalScore)
					{
						await EndSpeedupGame("You won!", false);
					}
				}
				else
				{
					await WrongButtonPressed(e.ButtonPress, false);
				}
			}
		}

		public (string shortVersion, string longVersion) GetFullDifficulty(int buttonCount)
		{
			return buttonCount switch
			{
				< 5 => ("1", "Small"),
				>= 10 => ("10", "Large"),
				_ => ("5", "Medium")
			};

		}

		public override async Task<bool> EndGame(string message, bool isFailure = false)
		{
			IsGameRunning = false;
			_logger.LogInformation(message);
			gameTimer?.Dispose();
			bool isHighScore = (await DbSaver.GetScoresWithNewScoreIfWarranted(GenerateScoreWithCurrentData())).Where(t => t.ScoreId == 0).Any(); ;
			if (isHighScore)
			{

				MsgSender.SendImageToAllButtons(new ButtonImage(PatternHelper.GetBytesForPattern(Patterns.AllOneColor), new(0, 255, 0)));
				await Task.Delay(3000);
				MsgSender.SendImageToAllButtons(new ButtonImage());
				MsgSender.SendUpdate("Game Ended", true, true);
			}
			else
			{
				MsgSender.SendImageToAllButtons(new ButtonImage(PatternHelper.GetBytesForPattern(Patterns.AllOneColor), new(255, 0, 0)));
				await Task.Delay(3000);
				MsgSender.SendImageToAllButtons(new ButtonImage());
				MsgSender.SendUpdate("Game Ended", true);
			}

			return true;
		}

		internal Score GenerateScoreWithCurrentData()
		{
			int buttonCount = buttons?.Count ?? 0;
			var buttonDifficulty = GetFullDifficulty(buttons?.Count ?? 0);
			decimal totalSeconds = (decimal)(GameEndedAt - GameStartedAt).TotalSeconds;
			string timeSpanString = (GameEndedAt - GameStartedAt).ToString("mm\\:ss");
			Score score = new Score()
			{
				DifficultyId = buttonDifficulty.shortVersion,
				DifficultyDescription = $"{buttonDifficulty.longVersion}",
				ScoreData = $"{buttonCount}",
				NormalizedScore = totalSeconds,
				ScoreDescription = $"Got to {goalScore} in {timeSpanString}"
			};
			return score;
		}

		public override ButtonImage GenerateNextButton()
		{
			return new ButtonImage(PatternHelper.GetBytesForPattern(Patterns.AllOneColor), colorToUse);
		}

		public override void Dispose()
		{
			if (ButtonTimer != null)
			{
				ButtonTimer.Dispose();
			}
			base.Dispose();
		}
	}
}
