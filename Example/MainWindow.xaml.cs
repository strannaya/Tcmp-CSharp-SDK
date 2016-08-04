﻿using System;

namespace TapTrack.Demo
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media.Imaging;
    using System.Collections.ObjectModel;
    using System.Threading;
    using WpfAnimatedGif;
    using System.Diagnostics;
    using Tcmp.Communication;
    using Tcmp.CommandFamilies;
    using Tcmp.CommandFamilies.BasicNfc;
    using Tcmp.CommandFamilies.Type4;
    using Ndef;
    using Tcmp.Communication.Exceptions;
    using Tcmp;
    using NdefLibrary.Ndef;
    using System.Text;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Driver tappyDriver;
        public ObservableCollection<Row> table;
        GridLength zeroHeight = new GridLength(0);

        public MainWindow()
        {
            InitializeComponent();
            tappyDriver = new Driver(CommunicationProtocol.Usb);
            table = new ObservableCollection<Row>();
            records.ItemsSource = table;
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            tappyDriver.Disconnect();
        }

        //
        // Read Ndef Message tab
        //

        private void ReadNdefButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPendingStatus("Waiting for tap");

            ndefData.Text = "";
            DetectSingleNdef detect = new DetectSingleNdef((byte)timeout.Value, DetectTagSetting.Type2Type4AandMifare);
            tappyDriver.SendCommand(detect, AddNdefContent);
        }

        private void AddNdefContent(ResponseFrame frame, Exception e)
        {
            if (CheckForErrorsOrTimeout(frame, e))
                return;

            byte[] data = frame.Data;

            byte[] temp = new byte[data.Length - data[1] - 2];

            Array.Copy(data, 2 + data[1], temp, 0, temp.Length);

            NdefMessage message = NdefMessage.FromByteArray(temp);

            Action update = () =>
            {
                foreach (NdefRecord record in message)
                {
                    ndefData.AppendText("Ndef Record:\n\n");

                    string type = Encoding.UTF8.GetString(record.Type);
                    ndefData.AppendText($"TNF: {record.TypeNameFormat.ToString()} ({(byte)record.TypeNameFormat})\n");
                    ndefData.AppendText($"Type: {type}\n");

                    if (record.Id != null)
                        ndefData.AppendText($"Type: {BitConverter.ToString(record.Id)}\n");

                    if (type.Equals("U"))
                    {
                        NdefUriRecord uriRecord = new NdefUriRecord(record);
                        ndefData.AppendText($"Payload: {uriRecord.Uri}\n");
                    }
                    else if (type.Equals("T"))
                    {
                        NdefTextRecord textRecord = new NdefTextRecord(record);
                        ndefData.AppendText($"Encoding: {textRecord.TextEncoding.ToString()}\n");
                        ndefData.AppendText($"Language: {textRecord.LanguageCode}\n");
                        ndefData.AppendText($"Payload: {textRecord.Text}\n");
                    }
                    else if (type.Contains("text"))
                    {
                        ndefData.AppendText($"Payload: {Encoding.UTF8.GetString(record.Payload)}\n");
                    }
                    else
                    {
                        ndefData.AppendText($"Payload: {BitConverter.ToString(record.Payload)}");
                    }

                    ndefData.AppendText($"----------\n");
                }
            };

            Dispatcher.BeginInvoke(update);
            ShowSuccessStatus();
        }

        //
        // Read UID Tab
        //

        private void ReadUIDButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPendingStatus("Waiting for tap");
            Command cmd = new DetectSingleTagUid((byte)timeout.Value, DetectTagSetting.Type2Type4AandMifare);
            tappyDriver.SendCommand(cmd, AddUID);
        }

        private void AddUID(ResponseFrame frame, Exception e)
        {
            if (CheckForErrorsOrTimeout(frame, e))
                return;

            Tag tag = new Tag(frame.Data);

            Action update = () =>
            {
                uidTextBox.Text = "";
                uidTextBox.Text += BitConverter.ToString(tag.UID);
                typeTextBox.Text = Tcmp.Tag.TypeLookUp(tag.TypeOfTag);
            };
            ShowSuccessStatus();
            Dispatcher.Invoke(update);
        }

        //
        // Write URI Tab
        //

        private void WriteURLButton_Click(object sender, RoutedEventArgs e)
        {
            string url = string.Copy(urlTextBox.Text);

            ShowPendingStatus("Waiting for tap");

            Command cmd = new WriteUri((byte)timeout.Value, (bool)lockCheckBox.IsChecked, new NdefUri(url));

            tappyDriver.SendCommand(cmd, ResponseCallback);
        }

        //
        // Write Text Tab
        //

        private void WriteTextButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPendingStatus("Waiting for tap");

            Command cmd = new WriteText((byte)timeout.Value, (bool)lockCheckBox.IsChecked, TextBox.Text);

            tappyDriver.SendCommand(cmd, ResponseCallback);
        }

        //
        // Write Multi Ndef Tab
        //

        private void WriteMultNdef(object send, RoutedEventArgs e)
        {
            NdefMessage message = new NdefMessage();

            foreach (Row row in table)
            {
                if (row.Selected.Equals("Text"))
                {
                    NdefTextRecord temp = new NdefTextRecord()
                    {
                        TextEncoding = NdefTextRecord.TextEncodingType.Utf8,
                        LanguageCode = "en",
                        Text = row.Content ?? ""
                    };
                    message.Add(temp);
                }
                else
                {
                    message.Add(new NdefUriRecord() { Uri = row.Content ?? "" });
                }
            }

            ShowPendingStatus("Waiting for tap");

            Command cmd = new WriteCustomNdef((byte)timeout.Value, (bool)lockCheckBox.IsChecked, message);

            tappyDriver.SendCommand(cmd, ResponseCallback);
        }

        private void AddTextRowButton_Click(object sender, RoutedEventArgs e)
        {
            Row row = new Row(table.Count);
            row.Selected = "Text";
            table.Add(row);
        }

        private void AddUriRowButton_Click(object sender, RoutedEventArgs e)
        {
            Row row = new Row(table.Count);
            row.Selected = "URI";
            table.Add(row);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            table.Clear();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            Button removeButton = sender as Button;
            int row = (int)removeButton.Tag;
            for (int i = row + 1; i < table.Count; i++)
                table[i].Index = i - 1;
            table.RemoveAt(row);
        }

        //
        // vCard Tab
        //

        private void WriteVCardButton_Click(object sender, RoutedEventArgs e)
        {
            //VCard info = new VCard(nameTextBox.Text, cellPhoneTextBox.Text, workPhoneTextBox.Text,
            //    homePhoneTextBox.Text, emailTextBox.Text, businessEmailTextBox.Text, homeAddrTextBox.Text,
            //    businessAddrTextBox.Text, companyTextBox.Text, titleTextBox.Text, websiteTextBox.Text);

            //ShowPendingStatus("Waiting for tap");

            //CustomRecordPayload record = new CustomRecordPayload(info.ToByteArray(), "text/x-vcard", TypeNameField.NfcForumWellKnown);
            //NdefMessage message = new NdefMessage(record);

            //Command cmd = new WriteCustomNdef((byte)timeout.Value, (bool)lockCheckBox.IsChecked, message);

            //tappyDriver.SendCommand(cmd, ResponseCallback);
        }

        private void ClearVCardButton_Click(object sender, RoutedEventArgs e)
        {
            nameTextBox.Text = "";
            emailTextBox.Text = "";
            cellPhoneTextBox.Text = "";
            homePhoneTextBox.Text = "";
            homeAddrTextBox.Text = "";
            websiteTextBox.Text = "";
            companyTextBox.Text = "";
            titleTextBox.Text = "";
            businessEmailTextBox.Text = "";
            workPhoneTextBox.Text = "";
            businessAddrTextBox.Text = "";
        }

        //
        // Detect Type 4B
        //

        private void ReadType4B(object sender, RoutedEventArgs e)
        {
            ShowPendingStatus("Waiting for tap");
            Command cmd = new DetectType4B((byte)timeout.Value);

            Callback responseCallback = (ResponseFrame frame, Exception exc) =>
            {
                if (CheckForErrorsOrTimeout(frame, exc))
                    return;

                if (frame.ResponseCode != 0x07)
                    return;

                byte[] data = frame.Data;

                Action update = () =>
                {
                    byte atqbLen = data[0];
                    byte attribLen = data[1];
                    atqbTextBox.Text = "";
                    attribTextBox.Text = "";


                    for (int i = 2; i < 2 + data[0]; i++)
                        atqbTextBox.Text += string.Format("{0:X}", data[i]).PadLeft(2, '0') + " ";

                    for (int i = data[0] + 2; i < data.Length; i++)
                        attribTextBox.Text += string.Format("{0:X}", data[i]).PadLeft(2, '0') + " ";
                };

                ShowSuccessStatus();
                Dispatcher.BeginInvoke(update);
            };

            tappyDriver.SendCommand(cmd, responseCallback);
        }

        private void UpdateDetTypeBForm(byte[] data)
        {
            Action update = () =>
            {
                byte atqbLen = data[0];
                byte attribLen = data[1];
                atqbTextBox.Text = "";
                attribTextBox.Text = "";


                for (int i = 2; i < 2 + data[0]; i++)
                    atqbTextBox.Text += string.Format("{0:X}", data[i]).PadLeft(2, '0') + " ";

                for (int i = data[0] + 2; i < data.Length; i++)
                    attribTextBox.Text += string.Format("{0:X}", data[i]).PadLeft(2, '0') + " ";
            };

            ShowSuccessStatus();
            Dispatcher.BeginInvoke(update);
        }

        //
        // Lock Tag Tab
        //

        public void LockButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPendingStatus("Waiting for tap");
            Command readCommand = new DetectSingleTagUid((byte)timeout.Value, DetectTagSetting.Type2Type4AandMifare);

            tappyDriver.SendCommand(readCommand, delegate (ResponseFrame frame, Exception exc)
            {
                if (CheckForErrorsOrTimeout(frame, exc))
                    return;

                Tag tag = new Tag(frame.Data);

                Action Lock = () =>
                {
                    Command lockCommand = new LockTag((byte)timeout.Value, tag.UID);

                    tappyDriver.SendCommand(lockCommand, ResponseCallback);
                };

                Dispatcher.BeginInvoke(Lock);
            });
        }

        //
        // Other
        //

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            HideStatus();
            tappyDriver.SendCommand<Stop>();
        }

        private void AutoDetectButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPendingStatus("Searching for a TappyUSB");

            Task.Run(() =>
            {
                if (tappyDriver.AutoDetect())
                    ShowSuccessStatus($"Connected to {tappyDriver.DeviceName}");
                else
                    ShowFailStatus("No TappyUSB found");
            });
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (settingsContainer.Height.IsAuto)
                settingsContainer.Height = zeroHeight;
            else
                settingsContainer.Height = GridLength.Auto;
        }

        private void ShowPendingStatus(string message)
        {
            statusPopup.IsOpen = true;
            statusText.Content = "Pending";
            statusMessage.Content = message;
            ImageBehavior.SetAnimatedSource(statusImage, (BitmapImage)FindResource("Pending"));
        }

        private void ShowSuccessStatus(string message = "")
        {
            Action show = () =>
            {
                statusPopup.IsOpen = true;
                statusText.Content = "Success";
                statusMessage.Content = message;
                ImageBehavior.SetAnimatedSource(statusImage, (BitmapImage)FindResource("Success"));

                Task.Run(() =>
                {
                    Thread.Sleep(750);
                    HideStatus();
                });
            };

            Dispatcher.BeginInvoke(show);
        }

        private void ShowFailStatus(string message)
        {
            Action show = () =>
            {
                dismissButtonContainer.Height = new GridLength(50);
                dismissButton.Visibility = Visibility.Visible;
                statusPopup.IsOpen = true;
                statusText.Content = "Fail";
                statusMessage.Content = message;
                ImageBehavior.SetAnimatedSource(statusImage, (BitmapImage)FindResource("Error"));
            };

            Dispatcher.BeginInvoke(show);
        }

        private void HideStatus()
        {
            Action hide = () =>
            {
                statusPopup.IsOpen = false;
            };

            Dispatcher.Invoke(hide);
        }

        private void DismissButton_Click(object sender, RoutedEventArgs e)
        {
            HideStatus();
            dismissButton.Visibility = Visibility.Hidden;
            dismissButtonContainer.Height = zeroHeight;
        }

        public void ResponseCallback(ResponseFrame frame, Exception e)
        {
            if (CheckForErrorsOrTimeout(frame, e))
                return;
            ShowSuccessStatus();
        }

        public bool CheckForErrorsOrTimeout(ResponseFrame frame, Exception e)
        {
            if (e != null)
            {
                if (e.GetType() == typeof(HardwareException))
                    ShowFailStatus("TappyUSB is not connected");
                else
                    ShowFailStatus("An error occured");

                return true;
            }
            else if (!TcmpFrame.IsValidFrame(frame))
            {
                ShowFailStatus("An error occured");

                return true;
            }
            else if (frame.IsApplicationErrorFrame())
            {
                ApplicationErrorFrame errorFrame = (ApplicationErrorFrame)frame;
                ShowFailStatus(errorFrame.ErrorString);
                return true;
            }
            else if (frame.CommandFamily0 == 0 && frame.CommandFamily1 == 0 && frame.ResponseCode < 0x05)
            {
                ShowFailStatus(TappyError.LookUp(frame.CommandFamily, frame.ResponseCode));
                return true;
            }
            else if (frame.ResponseCode == 0x03)
            {
                ShowFailStatus("No tag detected");
                return true;
            }
            else
            {
                return false;
            }
        }

        private void launchUrlButton_Click(object sender, RoutedEventArgs e)
        {
            DetectandLaunch();
        }

        private void DetectandLaunch()
        {
            Command cmd = new DetectSingleNdef(0, DetectTagSetting.Type2Type4AandMifare);
            tappyDriver.SendCommand(cmd, LaunchCallback);
        }

        private void LaunchCallback(ResponseFrame frame, Exception e)
        {
            if (CheckForErrorsOrTimeout(frame, e))
                return;

            byte[] data = frame.Data;
            byte[] temp = new byte[data.Length - data[1] - 2];

            Array.Copy(data, 2 + data[1], temp, 0, temp.Length);

            NdefMessage message = NdefMessage.FromByteArray(temp);

            if (message.Count > 0)
            {
                if (Encoding.UTF8.GetString(message[0].Type).Equals("U"))
                {
                    NdefUriRecord uriRecord = new NdefUriRecord(message[0]);
                    NdefUri uri = new NdefUri(uriRecord.Uri);
                    if (uri.Scheme == 0)
                        return;
                    Process.Start(uriRecord.Uri);
                }
            }

            Task.Run(() =>
            {
                Thread.Sleep(500);
                DetectandLaunch();
            });
        }

        private void configureTagForPlatform_Click(object sender, RoutedEventArgs e)
        {
            Command readCommand = new DetectSingleTagUid(0, DetectTagSetting.Type2Type4AandMifare);

            tappyDriver.SendCommand(readCommand, delegate (ResponseFrame frame, Exception exc)
            {
                if (exc != null)
                {
                    return;
                }
                else if (!TcmpFrame.IsValidFrame(frame))
                {
                    ShowFailStatus("Error occured");
                    return;
                }
                else if (frame.IsApplicationErrorFrame())
                {
                    ShowFailStatus(((ApplicationErrorFrame)frame).ErrorString);
                    return;
                }

                Tag tag = new Tag(frame.Data);
                string uid = BitConverter.ToString(tag.UID).Replace("-", "");
                string url = $"https://members.taptrack.com/m?id={uid}";
                Command write = new WriteUri(0, false, new NdefUri(url));

                Task.Run(() =>
                {
                    tappyDriver.SendCommand(write, ConfigSuccess);
                });
            });
        }

        private void ConfigSuccess(ResponseFrame frame, Exception e)
        {
            if (e != null)
            {
                return;
            }
            else if (!TcmpFrame.IsValidFrame(frame))
            {
                ShowFailStatus("Error occured");
                return;
            }
            else if (frame.IsApplicationErrorFrame())
            {
                ShowFailStatus(((ApplicationErrorFrame)frame).ErrorString);
                return;
            }

            Tag tag = new Tag(frame.Data);

            string uid = BitConverter.ToString(tag.UID).Replace("-", "");
            Process.Start(string.Format($"https://members.taptrack.com/x.php?tag_code={uid}"));
        }

        private void disconnectButton_Click(object sender, RoutedEventArgs e)
        {
            tappyDriver.Disconnect();
        }
    }
}
