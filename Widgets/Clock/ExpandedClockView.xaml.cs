using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;

namespace WDI.Widgets.Clock;

public sealed partial class ExpandedClockView : UserControl
{
    private DateTime _displayedMonth;

    public ExpandedClockView()
    {
        InitializeComponent();

        var now = DateTime.Now;
        _displayedMonth = new DateTime(now.Year, now.Month, 1);
        Update();
    }

    public void Update()
    {
        var now = DateTime.Now;
        ExpandedTimeText.Text = now.ToString("HH:mm");
        ExpandedDayText.Text = now.ToString("dddd");
        ExpandedDateText.Text = now.ToString("d MMMM yyyy");
        
        _displayedMonth = new DateTime(now.Year, now.Month, 1);
        BuildCalendar(now);
    }

    private void BuildCalendar(DateTime today)
    {
        CalendarMonthText.Text = _displayedMonth.ToString("MMMM yyyy");
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();

        for (int col = 0; col < 7; col++)
        {
            CalendarGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
        }

        for (int row = 0; row < 7; row++)
        {
            CalendarGrid.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            );
        }

        string[] weekdays = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
        for (int i = 0; i < weekdays.Length; i++)
        {
            var text = CreateDayText(weekdays[i], false);
            Grid.SetColumn(text, i);
            Grid.SetRow(text, 0);
            CalendarGrid.Children.Add(text);
        }

        int daysInMonth = DateTime.DaysInMonth(_displayedMonth.Year, _displayedMonth.Month);
        int firstDay = ((int)_displayedMonth.DayOfWeek + 6) % 7;
        for (int day = 1; day < daysInMonth; day++)
        {
            int index = firstDay + day - 1;
            int row = index / 7 + 1, col = index % 7;

            bool isToday = day == today.Day && _displayedMonth.Month == today.Month && _displayedMonth.Year == today.Year;
            var text = CreateDayText(day.ToString(), isToday);
            Grid.SetColumn(text, col);
            Grid.SetRow(text, row);
            CalendarGrid.Children.Add(text);
        }
    }

    private static TextBlock CreateDayText(string value, bool isToday)
    {
        var text = new TextBlock
        {
            Text = value,
            FontSize = 13,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        if (isToday) text.FontWeight = FontWeights.Bold;
        return text;
    }
}
