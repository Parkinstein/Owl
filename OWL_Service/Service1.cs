﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity.Migrations;
using System.Diagnostics;
using System.DirectoryServices;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Newtonsoft.Json;

namespace OWL_Service
{
    public partial class Owl_service : ServiceBase
    {
        #region Variables
        private Timer polling = new Timer();
        aspnetdbEntities databs = new aspnetdbEntities();
        public List<AllVMRS.AllVmrs> All_Vmrs;
        public AllVMRS.VmrParent All_VM_obj;
        public List<ActiveConfsModel.AConfs> AllConfs;
        public ActiveConfsModel.ResponseParent AllConfs_wm;
        public static Setting set;
        static bool mailSent = false;
        public int count;
        private string gp, gpmail;
        public static string GetProperty(SearchResult searchResult, string PropertyName)
        {
            if (searchResult.Properties.Contains(PropertyName))
            {
                return searchResult.Properties[PropertyName][0].ToString();
            }
            else
            {
                return string.Empty;
            }
        }
        private string Win1251ToUTF8(string source)
        {
            Encoding utf8 = Encoding.GetEncoding("windows-1251");
            Encoding win1251 = Encoding.GetEncoding("utf-8");
            byte[] utf8Bytes = win1251.GetBytes(source);
            byte[] win1251Bytes = Encoding.Convert(win1251, utf8, utf8Bytes);
            source = win1251.GetString(win1251Bytes);
            return source;
        }
        #endregion
        public Owl_service()
        {
            InitializeComponent();
        }
        protected override void OnStart(string[] args)
        {
            polling.Interval = 5000;
            polling.Enabled = true;
            polling.Elapsed += OnTimedEvent;
            polling.Start();
            Settings_Read();
        }
        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            GetPhonebookUsers();
            GetVmrList();
            GetNowMeeting();
            CheckForRecord();
        }
        public Setting Settings_Read()
        {
            var db = new aspnetdbEntities();
            set = db.Settings.FirstOrDefault();
            return set;
        }

        #region SyncData
        public List<ApplicationUser> GetPhonebookUsers()
        {
            List<ApplicationUser> allreco = new List<ApplicationUser>();
            try
            {
                string grname = "";
                string domainPath = String.Concat(set.AuthDnAddress, "/OU=", set.OU, ",DC=rad,DC=lan,DC=local");
                //"dc0.rad.lan.local/OU=Pepux,DC=rad,DC=lan,DC=local";
                DirectoryEntry directoryEntry = new DirectoryEntry("LDAP://" + domainPath, set.DnAdminUn,
                    set.DnAdminPass);
                DirectorySearcher dirSearcher = new DirectorySearcher(directoryEntry);
                dirSearcher.SearchScope = SearchScope.Subtree;
                dirSearcher.Filter = "(objectClass=user)";
                dirSearcher.PropertiesToLoad.Add("givenName");
                dirSearcher.PropertiesToLoad.Add("sn");
                dirSearcher.PropertiesToLoad.Add("title");
                dirSearcher.PropertiesToLoad.Add("telephoneNumber");
                dirSearcher.PropertiesToLoad.Add("sAMAccountName");
                dirSearcher.PropertiesToLoad.Add("displayName");
                dirSearcher.PropertiesToLoad.Add("mail");
                dirSearcher.PropertiesToLoad.Add("memberOf");
                dirSearcher.PropertiesToLoad.Add("ipPhone");
                SearchResultCollection resultCol = dirSearcher.FindAll();
                foreach (SearchResult resul in resultCol)
                {
                    ApplicationUser objSurveyUsers = new ApplicationUser();
                    objSurveyUsers.Name = GetProperty(resul, "givenName");
                    objSurveyUsers.Surname = GetProperty(resul, "sn"); 
                    objSurveyUsers.Tel_mob = GetProperty(resul, "telephoneNumber");
                    objSurveyUsers.Position = GetProperty(resul, "title"); 
                    objSurveyUsers.Email = GetProperty(resul, "mail"); 
                    objSurveyUsers.Sammaccount = GetProperty(resul, "sAMAccountName");
                    objSurveyUsers.DispName = GetProperty(resul, "displayName");
                    objSurveyUsers.Tel_int = GetProperty(resul, "ipPhone");
                    if (GetProperty(resul, "memberOf").Contains(set.AdminGroup))
                    {
                        grname = "Admins";
                    }
                    if (GetProperty(resul, "memberOf").Contains(set.UserGroup))
                    {
                        grname = "User";
                    }
                    objSurveyUsers.Group = grname;
                    allreco.Add(objSurveyUsers);
                }
                
                CompareUsers(allreco);
            }
            catch (Exception er)
            {
                
                Debug.WriteLine(er.HResult);
                Debug.WriteLine(er.Message);
            }
            return allreco;
        }
        private async void CompareUsers(List<ApplicationUser> allrecords)
        {
            var localusers = databs.AspNetUsers;
            List<string> locusr = new List<string>();
            List<string> adusr = new List<string>();
            List<ApplicationUser> locrecords = new List<ApplicationUser>();
            var manager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(new ApplicationDbContext()));
            
            foreach (var locuser in localusers)
            {
                locusr.Add(locuser.Sammaccount);
                locrecords.AddRange(manager.Users);
            }
            foreach (var domenuser in allrecords)
            {
                adusr.Add(domenuser.Sammaccount);
                if (!locusr.Contains(domenuser.Sammaccount))
                {
                    try
                    {
                        var user = domenuser;
                        await Register(user);
                    }
                    catch (Exception exe)
                    {
                        Debug.WriteLine(exe.InnerException);
                    }
                }
                if (locusr.Contains(domenuser.Sammaccount))
                {
                    var store = new UserStore<ApplicationUser>(new ApplicationDbContext());
                    var manager1 = new UserManager<ApplicationUser>(store);
                    var user =  manager1.FindByName(domenuser.Sammaccount);
                    await manager1.RemoveFromRolesAsync(user.Id, new string[] { "Admin", "User" });
                    user.DispName = domenuser.DispName;
                    user.Group = domenuser.Group;
                    user.H323_addr = domenuser.H323_addr;
                    user.Name = domenuser.Name;
                    user.Position = domenuser.Position;
                    user.Sip_addr = domenuser.Sip_addr;
                    user.Surname = domenuser.Surname;
                    user.Email = domenuser.Email;
                    user.Tel_int = domenuser.Tel_int;
                    user.Tel_mob = domenuser.Tel_mob;
                    await manager1.UpdateAsync(user);
                    if (user.Group == "Admins")
                    {
                        await manager1.AddToRoleAsync(user.Id, "Admin");
                    }
                    if (user.Group == "User")
                    {
                        await manager1.AddToRoleAsync(user.Id, "User");
                    }
                }
            }
            foreach (var lokuser in locrecords)
            {
                if (!adusr.Contains(lokuser.Sammaccount))
                {
                    try
                    {
                        var store = new UserStore<ApplicationUser>(new ApplicationDbContext());
                        var manager1 = new UserManager<ApplicationUser>(store);
                        var user = manager1.FindByName(lokuser.Sammaccount);
                        string[] deletegr = new string[] { "Admin", "User" };
                        await manager.RemoveFromRolesAsync(user.Id, deletegr);
                        await manager1.DeleteAsync(user);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        private async Task<ActionResult> Register(ApplicationUser model)
        {
            {
                var cont = new ApplicationDbContext();
                var user = new ApplicationUser()
                {
                    Position = model.Position,
                    Name = model.Name,
                    UserName = model.Sammaccount,
                    Email = model.Email,
                    DispName = model.DispName,
                    Sammaccount = model.Sammaccount,
                    Surname = model.Surname,
                    Group = model.Group
                };
                var manager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(new ApplicationDbContext()));
                IdentityResult result = await manager.CreateAsync(user,"1Q2w3e4r!");
                if (result.Succeeded)
                {
                    Debug.WriteLine("Есть!!   " + model.Group);
                    if (model.Group == "Admins")
                    {
                        try
                        {
                           await manager.AddToRoleAsync(user.Id,"Admin");
                            Debug.WriteLine("Добавлен в админы");
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine(e.InnerException);
                        }
                    }
                    if (model.Group == "User")
                    {
                        try
                        {
                            await manager.AddToRoleAsync(user.Id, "User");
                            Debug.WriteLine("Добавлен в юзвери");
                        }
                        catch (Exception e1)
                        {
                            Debug.WriteLine(user.Id);
                            Debug.WriteLine(e1.InnerException);
                        }
                    }
                    cont.SaveChanges();
                }
                else
                {
                    Debug.WriteLine("Хрень какая-то");
                }
            }
            return null;
        }
        public List<AllVMRS.AllVmrs> GetVmrList()
        {
            All_Vmrs = new List<AllVMRS.AllVmrs>();
            aspnetdbEntities dtbs = new aspnetdbEntities();
            List<int> remoteInts =new List<int>();
            List<int> localInts = new List<int>();
            Uri confapi = new Uri("https://" + set.CobaMngAddress + "/api/admin/configuration/v1/conference/");
            WebClient client = new WebClient();
            client.Credentials = new NetworkCredential(set.CobaMngLogin, set.CobaMngPass);
            string auth = String.Concat(set.CobaMngLogin,",", set.CobaMngPass);
            client.Headers.Add("auth", auth);
            client.Headers.Add("veryfy", "False");
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            string reply = client.DownloadString(confapi);
            string reply1 = Win1251ToUTF8(reply);
            if (!String.IsNullOrEmpty(reply))
            {
                All_VM_obj = JsonConvert.DeserializeObject<AllVMRS.VmrParent>(reply1);
                All_Vmrs = All_VM_obj.obj;
                foreach (var vm in All_Vmrs)
                {
                    AllVmr confroom = new AllVmr();
                    remoteInts.Add(vm.id);
                    confroom.Id = vm.id;
                    confroom.allow_guests = vm.allow_guests;
                    confroom.description = vm.description;
                    confroom.force_presenter_into_main = vm.force_presenter_into_main;
                    confroom.guest_pin = vm.guest_pin;
                    confroom.guest_view = vm.guest_view;
                    confroom.host_view = vm.host_view;
                    confroom.max_callrate_in = vm.max_callrate_in;
                    confroom.max_callrate_out = vm.max_callrate_out;
                    confroom.name = vm.name;
                    confroom.participant_limit = vm.participant_limit;
                    confroom.pin = vm.pin;
                    confroom.resource_uri = vm.resource_uri;
                    confroom.service_type = vm.service_type;
                    confroom.tag = vm.tag;
                    dtbs.AllVmrs.AddOrUpdate(confroom);
                    try
                    {
                        foreach (var ali in vm.aliases)
                        {
                            VmrAlias alias = new VmrAlias();
                            alias.Id = ali.id;
                            alias.alias = ali.alias;
                            alias.description = ali.description;
                            alias.conference = ali.conference;
                            alias.vmid = confroom.Id;
                            dtbs.VmrAliases.AddOrUpdate(alias);
                        }

                        #region Get IVR Themes - not yet working

                        //Ivr_Themes themes = new Ivr_Themes();
                        //themes.name = vm.ivr_theme.name;
                        //themes.intid = vm.ivr_theme.id;
                        //themes.uuid = vm.ivr_theme.uuid;
                        //themes.vmid = confroom.Id;
                        //dtbs.Ivr_Themes.AddOrUpdate(themes);

                        #endregion
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.InnerException);
                    }
                }
                foreach (var locrec in dtbs.AllVmrs)
                {
                    localInts.Add(locrec.Id);
                }
                foreach (var locint in localInts)
                {
                    if (!remoteInts.Contains(locint))
                    {
                        var delvmr = dtbs.AllVmrs.FirstOrDefault(v => v.Id == locint);
                        var delalias = dtbs.VmrAliases.Where(a => a.vmid == delvmr.Id);
                        dtbs.VmrAliases.RemoveRange(delalias);
                        dtbs.AllVmrs.Remove(delvmr);
                        Debug.WriteLine(delvmr.name);
                    }
                }
                try
                {
                    dtbs.SaveChanges();
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.InnerException);
                }
                return All_Vmrs;
            }
            return All_Vmrs;
        }
        #endregion

        #region CheckTimeMeeting
        public async void GetNowMeeting()
        {
            
            DateTime dt1 = DateTime.Now - TimeSpan.FromMinutes(180);
            DateTime dt2 = DateTime.Now - TimeSpan.FromMinutes(165);
            var soonmeet = databs.Meetings.Where(t => t.Start > dt1);
            var diapmet = soonmeet.Where(s => s.Start < dt2 && s.reminder);
            foreach (var meet in diapmet)
            {
                List<AspNetUser> maillist = new List<AspNetUser>();
                var attends = databs.MeetingAttendees.Where(m => m.MeetingID == meet.MeetingID);
                List<string> AddAtt;
                if (meet.AddAttend != null)
                {
                    AddAtt = ((meet.AddAttend.Replace(" ","")).Split((",").ToCharArray())).ToList();
                    foreach (var adat in AddAtt)
                    {
                        maillist.Add(new AspNetUser()
                        {
                            Email = adat,
                            DispName = adat
                        }
                                    );
                    }
                }
                foreach (var attend in attends)
                {
                    var reciever = databs.AspNetUsers.FirstOrDefault(u => u.Id == attend.AttendeeID);
                    maillist.Add(new AspNetUser()
                    {
                        Email = reciever.Email,
                        DispName = reciever.DispName
                    });
                }
                //string body1 = String.Concat("Напоминиаем, что через ", (meet.Start - dt1).ToString(@"mm"), " минут состоится видеоконференция ", "\"", meet.Title, "\"");
                var roomalias = databs.VmrAliases.FirstOrDefault(m => m.vmid == meet.RoomID);
                var currentroom = databs.AllVmrs.FirstOrDefault(m => m.Id == meet.RoomID);
                if (currentroom != null && !String.IsNullOrEmpty(currentroom.guest_pin))
                {
                    gpmail = "PIN для входа: " + currentroom.guest_pin;
                    gp = "&pin=" + currentroom.guest_pin;
                }
                if (currentroom != null && String.IsNullOrEmpty(currentroom.guest_pin))
                {
                    gpmail = "";
                    gp = "&role=guest";
                }
                foreach (var rcpt in maillist)
                {
                    Debug.WriteLine(rcpt.Email);
                    string link = "<a href =\"" + "https://" + set.CobaCfgAddress + "/webapp/?conference=" +
                              roomalias.alias + "&name=" + Uri.EscapeDataString(rcpt.DispName) + "&bw=512" + gp +
                              "&join=1" +
                              "\">ссылке</a>";
                    string body = "Уважамый(ая), " + rcpt.DispName + " !<br>" + "Напоминиаем, что через " +
                                  (meet.Start - dt1).ToString(@"mm")+" минут состоится конференция на тему \"" +
                                  meet.Title + "\"." + "<br>" + "Инициатор конференции: " + meet.FName + "<br>" +
                                  "В указанное время, для участия в конференции, просьба перейти по " +
                                  link + "<br><br>" + "<b><i>Данные для самостоятельного входа:<i><b><br> Адрес сервера: " +
                                  "https://" + set.CobaCfgAddress + "/" + "<br>" +
                                  "Имя конференции: " + roomalias.alias + "<br>" + gpmail + "<br>" + "SIP-адрес: " +
                                  roomalias.alias + "@" + set.CobaCfgAddress;
                    await Sendmail(rcpt.Email, "Напоминание о конференции", body, meet);
                    if (mailSent)
                    {
                        count++;
                        if (count == maillist.Count)
                        {
                            var db = new aspnetdbEntities();
                            meet.reminder = false;
                            db.Meetings.AddOrUpdate(meet);
                            db.SaveChanges();
                            
                        }
                        
                    }
                }
               
            }
        }
        public async Task Sendmail(string to, string subj, string body, Meeting meet)
        {
            SmtpClient smtpClient = new SmtpClient(set.SmtpServer, set.SmtpPort)
            {
                UseDefaultCredentials = false,
                EnableSsl = set.SmtpSSL,
                Credentials = new NetworkCredential(set.SmtpLogin, set.SmtpPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20000
            };
            MailMessage mailMessage = new MailMessage()
            {
                Priority = MailPriority.High,
                From = new MailAddress(set.MailFrom_email, set.MailFrom_name)
            };
            AlternateView alternateHtml = AlternateView.CreateAlternateViewFromString(body,
                                                                            new ContentType("text/html"));
            mailMessage.AlternateViews.Add(alternateHtml);
           
                mailMessage.To.Add(new MailAddress(to));
            
            mailMessage.Subject = subj;
            mailMessage.Body = body;
            smtpClient.SendCompleted += new
            SendCompletedEventHandler(SendCompletedCallback);

            await smtpClient.SendMailAsync(mailMessage);
        }
        
        private static void SendCompletedCallback(object sender, AsyncCompletedEventArgs e)
        {
            // Get the unique identifier for this asynchronous operation.
            string token = e.UserState.ToString();

            if (e.Cancelled)
            {
                Debug.WriteLine("[{0}] Send canceled.", token);
            }
            if (e.Error != null)
            {
                Debug.WriteLine("[{0}] {1}", token, e.Error.ToString());
            }
            else
            {
                Debug.WriteLine("Message sent.");
            }
            mailSent = true;
        }

        #endregion

        public void CheckForRecord()
        {
            DateTime dt1 = DateTime.Now - TimeSpan.FromMinutes(180);
            DateTime dt2 = DateTime.Now - TimeSpan.FromMinutes(175);
            var soonmeet = databs.Meetings.Where(t => t.Start > dt1);
            var diapmet = soonmeet.Where(s => s.Start < dt2 && s.Record);
            var allcon = new List<AllVMRS.AllVmrs>();
            aspnetdbEntities dtbs = new aspnetdbEntities();
            Uri confapi = new Uri("https://" + set.CobaMngAddress + "/api/admin/configuration/v1/conference/");
            WebClient client = new WebClient();
            client.Credentials = new NetworkCredential(set.CobaMngLogin, set.CobaMngPass);
            string auth = String.Concat(set.CobaMngLogin, ",", set.CobaMngPass);
            client.Headers.Add("auth", auth);
            client.Headers.Add("veryfy", "False");
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            string reply = Win1251ToUTF8(client.DownloadString(confapi));
            if (!String.IsNullOrEmpty(reply))
            {
                allcon = JsonConvert.DeserializeObject<AllVMRS.VmrParent>(reply).obj;
            }
            var actcon = GetActiveConfs();
            List<string> ids = new List<string>();
            foreach (Meeting meet in diapmet)
            {
                ids.Add((allcon.Where(m => m.id == meet.RoomID)).GetEnumerator().Current.name);
            }
            foreach (var id in ids)
            {
                
            }
        }
        public List<ActiveConfsModel.AConfs> GetActiveConfs()
        {
            try
            {
                AllConfs = new List<ActiveConfsModel.AConfs>();
                Uri statusapi = new Uri("https://" + set.CobaMngAddress + "/api/admin/status/v1/conference/");

                WebClient client = new WebClient();
                client.Credentials = new NetworkCredential(set.CobaMngLogin, set.CobaMngPass);
                client.Headers.Add("auth", set.CobaMngLogin + "," + set.CobaMngPass);
                client.Headers.Add("veryfy", "False");
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                string reply = client.DownloadString(statusapi);
                string reply1 = Win1251ToUTF8(reply);
                if (!String.IsNullOrEmpty(reply))
                {
                    AllConfs_wm = JsonConvert.DeserializeObject<ActiveConfsModel.ResponseParent>(reply1);
                    AllConfs = AllConfs_wm.obj;
                    foreach (var conf in AllConfs)
                    {
                        DateTime dt = DateTime.Parse(conf.start_time);
                        DateTime dt2 = dt + TimeSpan.FromHours(3);
                        string result = dt2.ToString("dd-MMM-yyyy  HH:mm:ss");
                        conf.start_time2 = result;
                        if (conf.is_locked)
                        {
                            conf.lock_path = "<img src=\"../images/lock.png\")\" style=\"max-width: 28px; max-height: 28px;\" />";
                        }
                        if (!conf.is_locked)
                        {
                            conf.lock_path = "<img src=\"../images/unlock.png\")\" style=\"max-width: 28px; max-height: 28px;\" />";
                        }

                    }
                }
                return AllConfs;
            }
            catch (Exception errException)
            {
                Debug.WriteLine(errException.Message);
            }
            return AllConfs;
        }
        protected override void OnStop()
        {
            polling.Dispose();
            polling.Stop();
        }
    }
}


