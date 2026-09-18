using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Sudoku.Mobile.Models;
using Sudoku.Mobile.Network;
using Sudoku.Mobile.Services;
using Sudoku.Shared.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile;

public partial class SudokuPage : ContentPage
{
    private static readonly Color Purple = Color.FromArgb("#7C3AED");
    private static readonly Color DarkPurple = Color.FromArgb("#4C1D95");
    private static readonly Color Lavender = Color.FromArgb("#EDE9FE");
    private static readonly Color TealHighlight = Color.FromArgb("#CFFAFE");
    private static readonly Color CellLine = Color.FromArgb("#C4B5FD");
    private static readonly Color EnteredNumber = Color.FromArgb("#7C3AED");
    private static readonly Color GivenNumber = Color.FromArgb("#1F2937");
    private static readonly Color IncorrectNumber = Color.FromArgb("#DC2626");

    private readonly int[,] _originalPuzzle = new int[9, 9];
    private readonly int[,] _myBoard = new int[9, 9];
    private readonly int[,] _opponentBoard = new int[9, 9];
    private readonly int[,] _solution = new int[9, 9];
    private readonly Button[,] _cellButtons = new Button[9, 9];
    private readonly Border[,] _cellBorders = new Border[9, 9];
    private readonly MatchService _matchService;
    private readonly IDispatcherTimer _gameTimer;
    private Button[] _numberButtons = Array.Empty<Button>();

    private int _selectedRow = -1;
    private int _selectedColumn = -1;
    private int _highlightedNumber;
    private int _unresolvedMistakeRow = -1;
    private int _unresolvedMistakeColumn = -1;
    private int _opponentMistakeRow = -1;
    private int _opponentMistakeColumn = -1;
    private int _localMistakeCount;
    private bool _showingOpponent;
    private bool _isSubmittingMove;
    private bool _developmentScreenshotCaptured;
    private bool _gameplayTrackingStopped;
    private bool _resultNavigationStarted;

    public SudokuPage()
    {
        InitializeComponent();

        _gameTimer = Dispatcher.CreateTimer();
        _gameTimer.Interval = TimeSpan.FromSeconds(1);
        _gameTimer.Tick += OnGameTimerTick;

        _numberButtons =
        [
            Number1, Number2, Number3, Number4, Number5,
            Number6, Number7, Number8, Number9
        ];

        StyleNumberPad();
        BuildSudokuGrid();

        _matchService = new MatchService(TcpGameClient.Shared);
        _matchService.OpponentProgressUpdated += OnOpponentProgressUpdated;
        _matchService.MatchUpdated += OnMatchUpdated;
        _matchService.MatchFinished += OnMatchFinished;

        bool loadedMatch = LoadMatchPuzzle();
        if (!loadedMatch)
            GenerateSudoku();

        SelectInitialCell();

        if (loadedMatch && MatchSession.CurrentMatch is { } match)
        {
            _matchService.TrackMatch(match.MatchId);
            DevelopmentLaunchOptions.MarkBoardReady(match.MatchId);
        }

        RefreshScreen();
        _gameTimer.Start();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_developmentScreenshotCaptured)
            return;

        _developmentScreenshotCaptured = true;
        await Task.Delay(1200);
        if (DevelopmentLaunchOptions.StageMistake)
            await RunDevelopmentStageMistakeAsync();
        await DevelopmentLaunchOptions.CaptureScreenshotAsync();
        if (DevelopmentLaunchOptions.RunBoardProbe)
            await RunDevelopmentBoardProbeAsync();
        if (DevelopmentLaunchOptions.ForceWin)
            await RunDevelopmentForceWinAsync();
    }

    protected override void OnDisappearing()
    {
        StopGameplayTracking();
        base.OnDisappearing();
    }

    private void StopGameplayTracking()
    {
        if (_gameplayTrackingStopped)
            return;

        _gameplayTrackingStopped = true;
        _gameTimer.Stop();
        _matchService.OpponentProgressUpdated -= OnOpponentProgressUpdated;
        _matchService.MatchUpdated -= OnMatchUpdated;
        _matchService.MatchFinished -= OnMatchFinished;
        _matchService.Dispose();
    }

    private void BuildSudokuGrid()
    {
        SudokuGrid.Children.Clear();

        for (int boxRow = 0; boxRow < 3; boxRow++)
        {
            for (int boxColumn = 0; boxColumn < 3; boxColumn++)
            {
                var box = new Grid
                {
                    BackgroundColor = Purple,
                    RowSpacing = 0,
                    ColumnSpacing = 0
                };

                for (int index = 0; index < 3; index++)
                {
                    box.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                    box.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                }

                for (int innerRow = 0; innerRow < 3; innerRow++)
                {
                    for (int innerColumn = 0; innerColumn < 3; innerColumn++)
                    {
                        int row = boxRow * 3 + innerRow;
                        int column = boxColumn * 3 + innerColumn;
                        var button = new Button
                        {
                            Padding = 0,
                            Margin = 0,
                            MinimumHeightRequest = 0,
                            MinimumWidthRequest = 0,
                            CornerRadius = 0,
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            BackgroundColor = Colors.White,
                            TextColor = GivenNumber,
                            CommandParameter = SudokuBoardCoordinates.ToIndex(row, column)
                        };
                        button.Clicked += OnCellClicked;
                        var cellBorder = new Border
                        {
                            Padding = 0,
                            Margin = 0,
                            Stroke = CellLine,
                            StrokeThickness = 0.75,
                            BackgroundColor = Colors.White,
                            Content = button
                        };
                        _cellButtons[row, column] = button;
                        _cellBorders[row, column] = cellBorder;
                        box.Add(cellBorder, innerColumn, innerRow);
                    }
                }

                SudokuGrid.Add(box, boxColumn, boxRow);
            }
        }
    }

    private bool LoadMatchPuzzle()
    {
        MatchStatusResponse? match = MatchSession.CurrentMatch;
        if (!SudokuBoardCoordinates.IsValidFlatBoard(match?.Puzzle!))
            return false;

        CopyFlatBoard(match!.Puzzle, _originalPuzzle);
        CopyFlatBoard(
            SudokuBoardCoordinates.IsValidFlatBoard(match.OwnBoard!)
                ? match.OwnBoard
                : match.Puzzle,
            _myBoard);
        CopyFlatBoard(
            SudokuBoardCoordinates.IsValidFlatBoard(match.OpponentBoard!)
                ? match.OpponentBoard
                : match.Puzzle,
            _opponentBoard);
        SynchronizeMistakes(match);
        return true;
    }

    private static void CopyFlatBoard(int[] source, int[,] destination)
    {
        for (int index = 0; index < 81; index++)
        {
            SudokuBoardCoordinates.FromIndex(index, out int row, out int column);
            destination[row, column] = source[index];
        }
    }

    private void OnOpponentProgressUpdated(object? sender, MoveResultResponse result)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MatchStatusResponse? match = MatchSession.CurrentMatch;
            if (match == null)
                return;

            match.OpponentCorrectCount = result.CorrectCount;
            match.OpponentErrorCount = result.ErrorCount;

            if (result.Accepted && result.BoardChanged &&
                result.Row is >= 0 and < 9 && result.Column is >= 0 and < 9)
            {
                _opponentBoard[result.Row, result.Column] = result.Value;
                if (match.OpponentBoard?.Length == 81)
                {
                    int index = SudokuBoardCoordinates.ToIndex(result.Row, result.Column);
                    match.OpponentBoard[index] = result.Value;
                }

                if (!result.IsCorrect && result.Value != 0)
                {
                    _opponentMistakeRow = result.Row;
                    _opponentMistakeColumn = result.Column;
                }
                else if (result.Value == 0 &&
                         result.Row == _opponentMistakeRow &&
                         result.Column == _opponentMistakeColumn)
                {
                    _opponentMistakeRow = -1;
                    _opponentMistakeColumn = -1;
                }
            }

            UpdateProgressDisplay();
            if (_showingOpponent)
                ApplyBoardVisuals();
        });
    }

    private void OnMatchUpdated(object? sender, MatchStatusResponse status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (IsFinalMatchState(status.State))
            {
                _ = NavigateToResultAsync(status);
                return;
            }

            string? opponentName = MatchSession.CurrentMatch?.OpponentName;
            if (String.IsNullOrWhiteSpace(status.OpponentName))
                status.OpponentName = opponentName;
            MatchSession.CurrentMatch = status;
            if (SudokuBoardCoordinates.IsValidFlatBoard(status.OwnBoard!))
                CopyFlatBoard(status.OwnBoard, _myBoard);
            if (SudokuBoardCoordinates.IsValidFlatBoard(status.OpponentBoard!))
                CopyFlatBoard(status.OpponentBoard, _opponentBoard);
            SynchronizeMistakes(status);
            UpdateInputAvailability();
            UpdateCellStatus();
            UpdateProgressDisplay();
            ApplyBoardVisuals();
        });
    }

    private void OnMatchFinished(object? sender, MatchStatusResponse status)
    {
        MainThread.BeginInvokeOnMainThread(() => _ = NavigateToResultAsync(status));
    }

    private async Task NavigateToResultAsync(MatchStatusResponse status)
    {
        if (_resultNavigationStarted)
            return;

        string? currentPlayerId = TcpGameClient.Shared.PlayerId;
        if (String.IsNullOrWhiteSpace(currentPlayerId))
            return;

        _resultNavigationStarted = true;
        string opponentName = GetOpponentName();
        if (String.IsNullOrWhiteSpace(status.OpponentName))
            status.OpponentName = opponentName;
        MatchSession.CurrentMatch = status;

        DuelResultData result = DuelResultSession.Create(
            status,
            currentPlayerId,
            opponentName);
        StopGameplayTracking();

        string route = result.IsVictory
            ? nameof(VictoryResultPage)
            : nameof(DefeatResultPage);
        await Shell.Current.GoToAsync(route);
    }

    private static bool IsFinalMatchState(MatchLifecycleState state) =>
        state is MatchLifecycleState.Finished or
            MatchLifecycleState.Aborted or
            MatchLifecycleState.Archived;

    private void OnMyBoardTabClicked(object sender, EventArgs e)
    {
        _showingOpponent = false;
        SyncHighlightToSelectedCell();
        RefreshScreen();
    }

    private void OnOpponentTabClicked(object sender, EventArgs e)
    {
        _showingOpponent = true;
        SyncHighlightToSelectedCell();
        RefreshScreen();
    }

    private void OnCellClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: int position })
            return;

        SudokuBoardCoordinates.FromIndex(position, out _selectedRow, out _selectedColumn);
        SyncHighlightToSelectedCell();
        UpdateCellStatus();
        ApplyBoardVisuals();
        ApplyNumberPadVisuals();
    }

    private async void OnNumberClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || !Int32.TryParse(button.Text, out int value))
            return;

        await EnterNumberAsync(value);
    }

    private async Task<bool> EnterNumberAsync(int value)
    {
        if (HasUnresolvedMistake || _showingOpponent || _isSubmittingMove ||
            !HasSelectedCell || _originalPuzzle[_selectedRow, _selectedColumn] != 0)
            return false;

        _highlightedNumber = value;
        int previousValue = _myBoard[_selectedRow, _selectedColumn];
        ApplyBoardVisuals();
        ApplyNumberPadVisuals();

        MatchStatusResponse? match = MatchSession.CurrentMatch;
        SetMyCellValue(value, match);
        ApplyBoardVisuals();

        if (match != null)
        {
            _isSubmittingMove = true;
            try
            {
                MoveResultResponse result = await _matchService.SubmitMoveAsync(
                    match.MatchId, _selectedRow, _selectedColumn, value);

                match.OwnCorrectCount = result.CorrectCount;
                match.OwnErrorCount = result.ErrorCount;

                if (!result.Accepted)
                {
                    SetMyCellValue(previousValue, match);
                    ApplyBoardVisuals();
                    return false;
                }

                if (!result.IsCorrect)
                    SetUnresolvedMistake(_selectedRow, _selectedColumn);
            }
            catch (Exception exception)
            {
                SetMyCellValue(previousValue, match);
                ApplyBoardVisuals();
                await DisplayAlertAsync("Connection error", exception.Message, "OK");
                return false;
            }
            finally
            {
                _isSubmittingMove = false;
            }
        }
        else if (value != _solution[_selectedRow, _selectedColumn])
        {
            _localMistakeCount++;
            SetUnresolvedMistake(_selectedRow, _selectedColumn);
        }

        UpdateInputAvailability();
        UpdateCellStatus();
        UpdateProgressDisplay();
        ApplyBoardVisuals();
        return true;
    }

    private void RefreshScreen()
    {
        string opponentName = GetOpponentName();
        MyBoardTabButton.Text = _showingOpponent ? "My Board" : "My Board     Active";
        MyBoardTabButton.BackgroundColor = _showingOpponent ? Colors.White : Purple;
        MyBoardTabButton.TextColor = _showingOpponent ? Color.FromArgb("#6B7280") : Colors.White;
        MyBoardTabButton.BorderColor = _showingOpponent ? Color.FromArgb("#DDD6FE") : Color.FromArgb("#5B21B6");

        OpponentTabButton.Text = _showingOpponent
            ? $"{opponentName}'s Board     Active"
            : $"{opponentName}'s Board   VIEW ONLY";
        OpponentTabButton.BackgroundColor = _showingOpponent ? Purple : Colors.White;
        OpponentTabButton.TextColor = _showingOpponent ? Colors.White : Color.FromArgb("#6B7280");
        OpponentTabButton.BorderColor = _showingOpponent ? Color.FromArgb("#5B21B6") : Color.FromArgb("#DDD6FE");
        OpponentReadOnlyBanner.IsVisible = _showingOpponent;

        UpdateInputAvailability();

        OpponentNameLabel.Text = opponentName;
        UpdateCellStatus();
        UpdateProgressDisplay();
        UpdateTimerDisplay();
        ApplyBoardVisuals();
        ApplyNumberPadVisuals();
    }

    private void ApplyBoardVisuals()
    {
        int[,] board = CurrentBoard;

        for (int row = 0; row < 9; row++)
        {
            for (int column = 0; column < 9; column++)
            {
                Button button = _cellButtons[row, column];
                Border cellBorder = _cellBorders[row, column];
                int value = board[row, column];
                bool selected = row == _selectedRow && column == _selectedColumn;
                bool sameNumber = _highlightedNumber != 0 && value == _highlightedNumber;
                bool related = HasSelectedCell &&
                    (row == _selectedRow || column == _selectedColumn ||
                     row / 3 == _selectedRow / 3 && column / 3 == _selectedColumn / 3);
                bool incorrect = _showingOpponent
                    ? row == _opponentMistakeRow && column == _opponentMistakeColumn
                    : row == _unresolvedMistakeRow && column == _unresolvedMistakeColumn;

                button.Text = value == 0 ? String.Empty : value.ToString();
                button.TextColor = _originalPuzzle[row, column] == 0
                    ? EnteredNumber
                    : GivenNumber;
                button.BackgroundColor = Colors.White;
                button.BorderWidth = 0;
                cellBorder.Stroke = CellLine;
                cellBorder.StrokeThickness = 0.75;
                cellBorder.ZIndex = 0;

                if (related)
                    button.BackgroundColor = Lavender;
                if (sameNumber)
                    button.BackgroundColor = TealHighlight;
                if (selected)
                {
                    button.BackgroundColor = Color.FromArgb("#DDD6FE");
                    button.TextColor = Color.FromArgb("#1E1B4B");
                    cellBorder.Stroke = Purple;
                    cellBorder.StrokeThickness = 2;
                    cellBorder.ZIndex = 10;
                }
                if (incorrect)
                    button.TextColor = IncorrectNumber;
            }
        }
    }

    private void StyleNumberPad()
    {
        foreach (Button button in _numberButtons)
        {
            button.HeightRequest = 40;
            button.MinimumHeightRequest = 0;
            button.MinimumWidthRequest = 0;
            button.Padding = 0;
            button.CornerRadius = 11;
            button.FontSize = 16;
            button.FontAttributes = FontAttributes.Bold;
            button.BackgroundColor = Colors.White;
            button.TextColor = DarkPurple;
            button.BorderColor = Color.FromArgb("#E2E8F0");
            button.BorderWidth = 2;
        }
    }

    private void ApplyNumberPadVisuals()
    {
        for (int index = 0; index < _numberButtons.Length; index++)
        {
            Button button = _numberButtons[index];
            bool highlighted = index + 1 == _highlightedNumber;
            button.BackgroundColor = highlighted ? Purple : Colors.White;
            button.TextColor = highlighted ? Colors.White : DarkPurple;
            button.BorderColor = highlighted
                ? Color.FromArgb("#5B21B6")
                : Color.FromArgb("#E2E8F0");
        }
    }

    private void UpdateProgressDisplay()
    {
        MatchStatusResponse? match = MatchSession.CurrentMatch;
        int cellsToSolve = 0;
        for (int row = 0; row < 9; row++)
            for (int column = 0; column < 9; column++)
                if (_originalPuzzle[row, column] == 0)
                    cellsToSolve++;

        int opponentCorrect = match?.OpponentCorrectCount ?? 0;
        int ownCorrect = match?.OwnCorrectCount ?? 0;
        double opponentProgress = cellsToSolve == 0
            ? 0
            : Math.Clamp((double)opponentCorrect / cellsToSolve, 0, 1);
        double ownProgress = cellsToSolve == 0
            ? 0
            : Math.Clamp((double)ownCorrect / cellsToSolve, 0, 1);
        int opponentPercent = (int)Math.Round(opponentProgress * 100);
        int ownPercent = (int)Math.Round(ownProgress * 100);

        OpponentProgressBar.Progress = opponentProgress;
        OpponentPercentLabel.Text = $"{opponentPercent}%";
        MyProgressLabel.Text = $"{ownPercent}%";

        int difference = ownPercent - opponentPercent;
        if (difference > 0)
        {
            LeadBadgeLabel.Text = $"You're leading! (+{difference}%)";
            LeadBadgeBorder.BackgroundColor = Color.FromArgb("#DCFCE7");
            LeadBadgeBorder.Stroke = Color.FromArgb("#BBF7D0");
            LeadBadgeLabel.TextColor = Color.FromArgb("#15803D");
        }
        else if (difference < 0)
        {
            LeadBadgeLabel.Text = $"{GetOpponentName()} is leading! ({difference}%)";
            LeadBadgeBorder.BackgroundColor = Color.FromArgb("#FEF3C7");
            LeadBadgeBorder.Stroke = Color.FromArgb("#FDE68A");
            LeadBadgeLabel.TextColor = Color.FromArgb("#D97706");
        }
        else
        {
            LeadBadgeLabel.Text = "Match is tied";
            LeadBadgeBorder.BackgroundColor = Color.FromArgb("#FEF3C7");
            LeadBadgeBorder.Stroke = Color.FromArgb("#FDE68A");
            LeadBadgeLabel.TextColor = Color.FromArgb("#D97706");
        }
    }

    private async void OnEraseClicked(object? sender, EventArgs e)
    {
        await EraseSelectedCellAsync();
    }

    private async Task<bool> EraseSelectedCellAsync()
    {
        if (_showingOpponent || _isSubmittingMove)
            return false;

        int eraseRow = HasUnresolvedMistake ? _unresolvedMistakeRow : _selectedRow;
        int eraseColumn = HasUnresolvedMistake ? _unresolvedMistakeColumn : _selectedColumn;
        if (eraseRow is < 0 or > 8 || eraseColumn is < 0 or > 8 ||
            _originalPuzzle[eraseRow, eraseColumn] != 0 ||
            _myBoard[eraseRow, eraseColumn] == 0)
            return false;

        MatchStatusResponse? match = MatchSession.CurrentMatch;
        if (match != null)
        {
            _isSubmittingMove = true;
            try
            {
                MoveResultResponse result = await _matchService.SubmitMoveAsync(
                    match.MatchId, eraseRow, eraseColumn, 0);
                if (!result.Accepted)
                    return false;
                match.OwnCorrectCount = result.CorrectCount;
                match.OwnErrorCount = result.ErrorCount;
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Connection error", exception.Message, "OK");
                return false;
            }
            finally
            {
                _isSubmittingMove = false;
            }
        }

        SetMyCellValue(eraseRow, eraseColumn, 0, match);
        if (eraseRow == _unresolvedMistakeRow && eraseColumn == _unresolvedMistakeColumn)
            ClearUnresolvedMistake();
        _highlightedNumber = 0;
        UpdateInputAvailability();
        UpdateCellStatus();
        UpdateProgressDisplay();
        ApplyBoardVisuals();
        ApplyNumberPadVisuals();
        return true;
    }

    private async void OnHintClicked(object? sender, EventArgs e)
    {
        if (HasUnresolvedMistake)
            return;

        await DisplayAlertAsync(
            "Ranked match",
            "Hints are disabled during a competitive duel.",
            "OK");
    }

    private void UpdateCellStatus()
    {
        string cell = HasSelectedCell
            ? $"Cell: R{_selectedRow + 1} C{_selectedColumn + 1}"
            : "Cell: --";
        CellStatusLabel.Text = $"{cell}     Mistakes: {CurrentMistakeCount}";
    }

    private void SetMyCellValue(int value, MatchStatusResponse? match)
    {
        SetMyCellValue(_selectedRow, _selectedColumn, value, match);
    }

    private void SetMyCellValue(int row, int column, int value, MatchStatusResponse? match)
    {
        _myBoard[row, column] = value;
        if (match?.OwnBoard?.Length == 81)
            match.OwnBoard[SudokuBoardCoordinates.ToIndex(
                row,
                column)] = value;
    }

    private void SynchronizeMistakes(MatchStatusResponse match)
    {
        _unresolvedMistakeRow = match.OwnHasUnresolvedMistake
            ? match.OwnUnresolvedMistakeRow
            : -1;
        _unresolvedMistakeColumn = match.OwnHasUnresolvedMistake
            ? match.OwnUnresolvedMistakeColumn
            : -1;
        _opponentMistakeRow = match.OpponentHasUnresolvedMistake
            ? match.OpponentUnresolvedMistakeRow
            : -1;
        _opponentMistakeColumn = match.OpponentHasUnresolvedMistake
            ? match.OpponentUnresolvedMistakeColumn
            : -1;
    }

    private void SetUnresolvedMistake(int row, int column)
    {
        _unresolvedMistakeRow = row;
        _unresolvedMistakeColumn = column;
        if (MatchSession.CurrentMatch is { } match)
        {
            match.OwnHasUnresolvedMistake = true;
            match.OwnUnresolvedMistakeRow = row;
            match.OwnUnresolvedMistakeColumn = column;
        }
    }

    private void ClearUnresolvedMistake()
    {
        _unresolvedMistakeRow = -1;
        _unresolvedMistakeColumn = -1;
        if (MatchSession.CurrentMatch is { } match)
        {
            match.OwnHasUnresolvedMistake = false;
            match.OwnUnresolvedMistakeRow = -1;
            match.OwnUnresolvedMistakeColumn = -1;
        }
    }

    private void UpdateInputAvailability()
    {
        bool numberEntryEnabled = !_showingOpponent && !HasUnresolvedMistake;
        foreach (Button button in _numberButtons)
            button.IsEnabled = numberEntryEnabled;
        EraseButton.IsEnabled = !_showingOpponent;
        HintButton.IsEnabled = numberEntryEnabled;
        NumberPadCard.Opacity = numberEntryEnabled ? 1 : 0.40;
        EraseButton.Opacity = _showingOpponent ? 0.40 : 1;
        HintButton.Opacity = numberEntryEnabled ? 1 : 0.40;
    }

    private string GetOpponentName()
    {
        string? name = MatchSession.CurrentMatch?.OpponentName;
        return String.IsNullOrWhiteSpace(name) ? "Rival" : name;
    }

    private void OnGameTimerTick(object? sender, EventArgs e)
    {
        UpdateTimerDisplay();
    }

    private void UpdateTimerDisplay()
    {
        MatchStatusResponse? match = MatchSession.CurrentMatch;
        TimeSpan remaining = match?.EndsAtUtc is DateTime endsAtUtc
            ? endsAtUtc - DateTime.UtcNow
            : match?.TimeLeft ?? TimeSpan.Zero;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;
        GameTimerLabel.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0)
            return;

        double layoutWidth = Math.Clamp(Width - 16, 306, 424);
        GameplayLayout.WidthRequest = layoutWidth;
        double containerSize = Math.Clamp(layoutWidth - 22, 290, 370);
        BoardContainer.WidthRequest = containerSize;
        BoardContainer.HeightRequest = containerSize;
        BoardContainer.MinimumWidthRequest = containerSize;
        BoardContainer.MinimumHeightRequest = containerSize;
        BoardContainer.MaximumWidthRequest = containerSize;
        BoardContainer.MaximumHeightRequest = containerSize;
        double gridSize = containerSize - 18;
        SudokuGrid.WidthRequest = gridSize;
        SudokuGrid.HeightRequest = gridSize;
        SudokuGrid.MinimumWidthRequest = gridSize;
        SudokuGrid.MinimumHeightRequest = gridSize;
        SudokuGrid.MaximumWidthRequest = gridSize;
        SudokuGrid.MaximumHeightRequest = gridSize;
    }

    private async Task RunDevelopmentBoardProbeAsync()
    {
        var results = new List<string>
        {
            $"startedAt={DateTimeOffset.Now:O}"
        };

        bool allHitboxesMapped = true;
        for (int row = 0; row < 9; row++)
        {
            for (int column = 0; column < 9; column++)
            {
                OnCellClicked(_cellButtons[row, column], EventArgs.Empty);
                allHitboxesMapped &= _selectedRow == row && _selectedColumn == column;
            }
        }
        results.Add($"all81HitboxesMapped={allHitboxesMapped}");

        int editableRow = -1;
        int editableColumn = -1;
        int secondEditableRow = -1;
        int secondEditableColumn = -1;
        int givenRow = -1;
        int givenColumn = -1;
        for (int row = 0; row < 9; row++)
        {
            for (int column = 0; column < 9; column++)
            {
                if (_originalPuzzle[row, column] == 0 && editableRow < 0)
                {
                    editableRow = row;
                    editableColumn = column;
                }
                else if (_originalPuzzle[row, column] == 0 && secondEditableRow < 0)
                {
                    secondEditableRow = row;
                    secondEditableColumn = column;
                }
                else if (_originalPuzzle[row, column] != 0 && givenRow < 0)
                {
                    givenRow = row;
                    givenColumn = column;
                }
            }
        }

        bool wrongAnswerLockedInput = editableRow >= 0 && secondEditableRow >= 0;
        bool eraseResolvedMistake = false;
        bool correctAnswerAccepted = false;
        if (wrongAnswerLockedInput)
        {
            var probeSolution = new int[9, 9];
            Array.Copy(_originalPuzzle, probeSolution, _originalPuzzle.Length);
            wrongAnswerLockedInput = FillBoard(probeSolution);
            OnCellClicked(_cellButtons[editableRow, editableColumn], EventArgs.Empty);
            int correctValue = probeSolution[editableRow, editableColumn];
            int wrongValue = correctValue == 9 ? 1 : correctValue + 1;
            int mistakesBefore = CurrentMistakeCount;
            bool wrongAccepted = await EnterNumberAsync(wrongValue);

            OnCellClicked(_cellButtons[secondEditableRow, secondEditableColumn], EventArgs.Empty);
            bool blocked = !await EnterNumberAsync(1);
            wrongAnswerLockedInput = wrongAccepted && blocked && HasUnresolvedMistake &&
                _myBoard[editableRow, editableColumn] == wrongValue &&
                _cellButtons[editableRow, editableColumn].TextColor == IncorrectNumber &&
                CurrentMistakeCount == mistakesBefore + 1;

            eraseResolvedMistake = await EraseSelectedCellAsync() &&
                !HasUnresolvedMistake &&
                _myBoard[editableRow, editableColumn] == 0 &&
                CurrentMistakeCount == mistakesBefore + 1;

            OnCellClicked(_cellButtons[editableRow, editableColumn], EventArgs.Empty);
            correctAnswerAccepted = await EnterNumberAsync(correctValue) &&
                _myBoard[editableRow, editableColumn] == correctValue &&
                !HasUnresolvedMistake;
        }
        results.Add($"wrongAnswerLockedInput={wrongAnswerLockedInput}");
        results.Add($"eraseResolvedMistake={eraseResolvedMistake}");
        results.Add($"correctAnswerAccepted={correctAnswerAccepted}");

        bool givenLocked = givenRow >= 0;
        if (givenLocked)
        {
            int originalValue = _myBoard[givenRow, givenColumn];
            OnCellClicked(_cellButtons[givenRow, givenColumn], EventArgs.Empty);
            bool accepted = await EnterNumberAsync(originalValue == 9 ? 1 : originalValue + 1);
            givenLocked = !accepted && _myBoard[givenRow, givenColumn] == originalValue;
        }
        results.Add($"originalGivenLocked={givenLocked}");

        bool opponentReadOnly = editableRow >= 0;
        bool tabStatePreserved = editableRow >= 0;
        if (editableRow >= 0)
        {
            int ownValueBeforeTabSwitch = _myBoard[editableRow, editableColumn];
            int opponentValueBeforeInput = _opponentBoard[editableRow, editableColumn];

            OnOpponentTabClicked(OpponentTabButton, EventArgs.Empty);
            OnCellClicked(_cellButtons[editableRow, editableColumn], EventArgs.Empty);
            bool acceptedOnOpponent = await EnterNumberAsync(5);
            opponentReadOnly = _showingOpponent &&
                !acceptedOnOpponent &&
                _opponentBoard[editableRow, editableColumn] == opponentValueBeforeInput &&
                _myBoard[editableRow, editableColumn] == ownValueBeforeTabSwitch;

            OnMyBoardTabClicked(MyBoardTabButton, EventArgs.Empty);
            tabStatePreserved = !_showingOpponent &&
                _myBoard[editableRow, editableColumn] == ownValueBeforeTabSwitch;
        }
        results.Add($"opponentReadOnly={opponentReadOnly}");
        results.Add($"tabStatePreserved={tabStatePreserved}");

        bool everyCellHasBorder = true;
        for (int row = 0; row < 9; row++)
            for (int column = 0; column < 9; column++)
                everyCellHasBorder &= _cellBorders[row, column] != null &&
                    _cellBorders[row, column].StrokeThickness > 0;
        results.Add($"all81CellsHaveBorders={everyCellHasBorder}");
        results.Add($"completedAt={DateTimeOffset.Now:O}");
        DevelopmentLaunchOptions.MarkBoardProbe(results);
    }

    private async Task RunDevelopmentStageMistakeAsync()
    {
        if (HasUnresolvedMistake)
            return;

        var solvedPuzzle = new int[9, 9];
        Array.Copy(_originalPuzzle, solvedPuzzle, _originalPuzzle.Length);
        if (!FillBoard(solvedPuzzle))
            return;

        int mistakeRow = -1;
        int mistakeColumn = -1;
        for (int row = 3; row <= 5 && mistakeRow < 0; row++)
        {
            for (int column = 0; column < 9; column++)
            {
                if (_originalPuzzle[row, column] != 0 || _myBoard[row, column] != 0)
                    continue;
                mistakeRow = row;
                mistakeColumn = column;
                break;
            }
        }

        if (mistakeRow < 0)
        {
            for (int row = 0; row < 9 && mistakeRow < 0; row++)
            for (int column = 0; column < 9; column++)
            {
                if (_originalPuzzle[row, column] != 0 || _myBoard[row, column] != 0)
                    continue;
                mistakeRow = row;
                mistakeColumn = column;
                break;
            }
        }

        if (mistakeRow < 0)
            return;

        OnCellClicked(_cellButtons[mistakeRow, mistakeColumn], EventArgs.Empty);
        int correctValue = solvedPuzzle[mistakeRow, mistakeColumn];
        int wrongValue = correctValue == 9 ? 1 : correctValue + 1;
        await EnterNumberAsync(wrongValue);
        await GameplayScrollView.ScrollToAsync(
            BoardContainer,
            ScrollToPosition.Start,
            false);
    }

    private async Task RunDevelopmentForceWinAsync()
    {
        Array.Copy(_originalPuzzle, _solution, _originalPuzzle.Length);
        if (!FillBoard(_solution))
            return;

        for (int row = 0; row < 9 && !_resultNavigationStarted; row++)
        {
            for (int column = 0; column < 9 && !_resultNavigationStarted; column++)
            {
                if (_originalPuzzle[row, column] != 0)
                    continue;

                OnCellClicked(_cellButtons[row, column], EventArgs.Empty);
                await EnterNumberAsync(_solution[row, column]);
            }
        }
    }

    private void SyncHighlightToSelectedCell()
    {
        _highlightedNumber = HasSelectedCell
            ? CurrentBoard[_selectedRow, _selectedColumn]
            : 0;
    }

    private void SelectInitialCell()
    {
        for (int row = 0; row < 9; row++)
        {
            for (int column = 0; column < 9; column++)
            {
                if (_myBoard[row, column] == 0)
                    continue;
                _selectedRow = row;
                _selectedColumn = column;
                _highlightedNumber = _myBoard[row, column];
                return;
            }
        }
    }

    private bool HasSelectedCell =>
        _selectedRow is >= 0 and < 9 && _selectedColumn is >= 0 and < 9;

    private bool HasUnresolvedMistake =>
        _unresolvedMistakeRow is >= 0 and < 9 &&
        _unresolvedMistakeColumn is >= 0 and < 9;

    private int CurrentMistakeCount =>
        MatchSession.CurrentMatch?.OwnErrorCount ?? _localMistakeCount;

    private int[,] CurrentBoard => _showingOpponent ? _opponentBoard : _myBoard;

    private void GenerateSudoku()
    {
        GenerateSolvedBoard();
        Array.Copy(_solution, _originalPuzzle, _solution.Length);

        int removed = 0;
        while (removed < 40)
        {
            int row = Random.Shared.Next(0, 9);
            int column = Random.Shared.Next(0, 9);
            if (_originalPuzzle[row, column] == 0)
                continue;
            _originalPuzzle[row, column] = 0;
            removed++;
        }

        Array.Copy(_originalPuzzle, _myBoard, _originalPuzzle.Length);
        Array.Copy(_originalPuzzle, _opponentBoard, _originalPuzzle.Length);
    }

    private void GenerateSolvedBoard()
    {
        Array.Clear(_solution);
        FillBoard(_solution);
    }

    private static bool FillBoard(int[,] board)
    {
        for (int row = 0; row < 9; row++)
        {
            for (int column = 0; column < 9; column++)
            {
                if (board[row, column] != 0)
                    continue;

                int[] numbers = [1, 2, 3, 4, 5, 6, 7, 8, 9];
                Random.Shared.Shuffle(numbers);
                foreach (int number in numbers)
                {
                    if (!IsValid(board, row, column, number))
                        continue;
                    board[row, column] = number;
                    if (FillBoard(board))
                        return true;
                    board[row, column] = 0;
                }
                return false;
            }
        }
        return true;
    }

    private static bool IsValid(int[,] board, int row, int column, int number)
    {
        for (int index = 0; index < 9; index++)
            if (board[row, index] == number || board[index, column] == number)
                return false;

        int startRow = row / 3 * 3;
        int startColumn = column / 3 * 3;
        for (int checkRow = startRow; checkRow < startRow + 3; checkRow++)
            for (int checkColumn = startColumn; checkColumn < startColumn + 3; checkColumn++)
                if (board[checkRow, checkColumn] == number)
                    return false;
        return true;
    }
}
