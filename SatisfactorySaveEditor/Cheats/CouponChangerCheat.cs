using SatisfactorySaveEditor.Model;
using SatisfactorySaveEditor.Properties;
using SatisfactorySaveEditor.View;
using SatisfactorySaveEditor.ViewModel;
using SatisfactorySaveEditor.ViewModel.Property;

using SatisfactorySaveParser;

using System;
using System.Windows;
using static System.Math;

namespace SatisfactorySaveEditor.Cheats
{
    public class CouponChangerCheat : ICheat
    {
        public string NameKey => "CheatSetCoupons";

        private long pointsRequiredFromTicketCount(int tickets)
        {
            //equation for ticket count from points is y={x>3:(ceil(x/3)^2)*1000, x<4:1000} where x is ticket count and y is points required. from here: https://satisfactory.gamepedia.com/AWESOME_Sink
            //OLD ticket cost function for pre-0.3.3 AWESOME sink --TODO update to new ticket cost function once that is determined
            if (tickets < 4)
                return 1000;
            else
                return (long) (Pow(Ceiling(tickets / 3.0), 2) * 1000);
        }

        public bool Apply(SaveObjectModel rootItem, SatisfactorySave saveGame)
        {
            var sinkSubsystem = rootItem.FindChild("Persistent_Level:PersistentLevel.ResourceSinkSubsystem", false);
            if (sinkSubsystem == null)
            {
                MessageBox.Show(Resources.MsgNoResourceSinkSubsystem_Body, Resources.MsgNoResourceSinkSubsystem_Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var pointsTowardsCurrentTicket = sinkSubsystem.FindOrCreateField<Int64PropertyViewModel>("mTotalResourceSinkPoints");
            var mCurrentPointLevel = sinkSubsystem.FindOrCreateField<IntPropertyViewModel>("mCurrentPointLevel");

            var dialog = new StringPromptWindow
            {
                Owner = Application.Current.MainWindow
            };
            var cvm = (StringPromptViewModel)dialog.DataContext;
            cvm.WindowTitle = Resources.PromptCoupon_Title;
            cvm.PromptMessage = Resources.PromptCoupon_Caption;
            cvm.ValueChosen = "0";
            cvm.OldValueMessage = string.Format(Resources.PromptCoupon_Detail, mCurrentPointLevel.Value);
            dialog.ShowDialog();

            int requestedTicketCount = 0;

            try
            {
                requestedTicketCount = int.Parse(cvm.ValueChosen);

                if (requestedTicketCount < 0)
                {
                    MessageBox.Show(Resources.MsgCouponMustBePositive_Body, Resources.MsgCouponUnchanged_Body, MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                else if (requestedTicketCount > 288100000)
                {
                    MessageBox.Show(Resources.MsgCouponMaxExceeded_Body, Resources.MsgCouponMaxExceeded_Title);
                    return false;
                }

                mCurrentPointLevel.Value = requestedTicketCount; //"point level" is 0 if no tickets have been earned, 1 if one ticket has, etc.

                pointsTowardsCurrentTicket.Value = 0; //reset progress towards the current ticket so the game GUI doesn't get confused

                long calculatedPointsCount = pointsRequiredFromTicketCount(requestedTicketCount);

                MessageBox.Show(string.Format(Resources.MsgCouponSet_Body, requestedTicketCount), Resources.MsgSuccess_Title, MessageBoxButton.OK, MessageBoxImage.Information);
                //MessageBox.Show($"Ticket count set to {requestedTicketCount}. The next ticket will take {calculatedPointsCount} points to earn.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception)
            {
                if (!(cvm.ValueChosen == "cancel"))
                {
                    MessageBox.Show(string.Format(Resources.MsgCouldNotParse_Body, cvm.ValueChosen));
                }
                return false;
            }
        }
    }
}
