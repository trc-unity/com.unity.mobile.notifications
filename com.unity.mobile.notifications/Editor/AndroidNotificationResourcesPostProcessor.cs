#if UNITY_EDITOR && PLATFORM_ANDROID
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;

namespace Unity.Notifications
{
    public class AndroidNotificationResourcesPostProcessor : IPostGenerateGradleAndroidProject
    {
        const string kAndroidNamespaceURI = "http://schemas.android.com/apk/res/android";

        public int callbackOrder { get { return 0; } }

        public void OnPostGenerateGradleAndroidProject(string projectPath)
        {
            InsertGradleDependencies(projectPath);

            CopyNotificationResources(projectPath);

            InjectAndroidManifest(projectPath);
        }

        // Insert dependencies that need by mobile notification package.
        private void InsertGradleDependencies(string projectPath)
        {
            // Here always insert a '\n' at the beginning, as in gradle:
            //  1. for dependencies, you can put '}' at the end of the last 'implementation' line;
            //  2. but you can't put two 'implementation's in one line.
            // so just always add a new line to make sure it work for all cases.
            const string kDependency = "\n    implementation 'com.android.support:appcompat-v7:27.1.1'\n";

            var gradleFilePath = Path.Combine(projectPath, "build.gradle");
            if (!File.Exists(gradleFilePath))
                return;

            var content = File.ReadAllText(gradleFilePath);
            if (string.IsNullOrEmpty(content))
                return;

            // Find the first '}' after 'dependencies' which has 'implementation' come after.
            var regex = new Regex(@"dependencies[\s\S]+?implementation[\s\S]+?(?<index>}+?)");
            var result = regex.Match(content);
            if (result.Success)
                File.WriteAllText(gradleFilePath, content.Insert(result.Groups["index"].Index, kDependency));
        }

        private void CopyNotificationResources(string projectPath)
        {
            // The projectPath points to the the parent folder instead of the actual project path.
            if (!Directory.Exists(Path.Combine(projectPath, "src")))
            {
                projectPath = Path.Combine(projectPath, PlayerSettings.productName);
            }

            // Get the icons set in the UnityNotificationEditorManager and write them to the res folder, then we can use the icons as res.
            var icons = UnityNotificationEditorManager.Initialize().GenerateDrawableResourcesForExport();
            foreach (var icon in icons)
            {
                var fileInfo = new FileInfo(string.Format("{0}/src/main/res/{1}", projectPath, icon.Key));
                if (fileInfo.Directory != null)
                {
                    fileInfo.Directory.Create();
                    File.WriteAllBytes(fileInfo.FullName, icon.Value);
                }
            }
        }

        private void InjectAndroidManifest(string projectPath)
        {
            var manifestPath = string.Format("{0}/src/main/AndroidManifest.xml", projectPath);
            if (!File.Exists(manifestPath))
                return;

            XmlDocument manifestDoc = new XmlDocument();
            manifestDoc.Load(manifestPath);

            InjectReceivers(manifestDoc);

            var settings = UnityNotificationEditorManager.Initialize().AndroidNotificationEditorSettingsFlat;

            var useCustomActivity = (bool)settings.Find(i => i.key == "UnityNotificationAndroidUseCustomActivity").val;
            if (useCustomActivity)
            {
                var customActivity = (string)settings.Find(i => i.key == "UnityNotificationAndroidCustomActivityString").val;
                AppendAndroidMetadataField(manifestDoc, "custom_notification_android_activity", customActivity);
            }

            var enableRescheduleOnRestart = (bool)settings.Find(i => i.key == "UnityNotificationAndroidRescheduleOnDeviceRestart").val;
            if (enableRescheduleOnRestart)
            {
                AppendAndroidMetadataField(manifestDoc, "reschedule_notifications_on_restart", "true");
                AppendAndroidPermissionField(manifestDoc, "android.permission.RECEIVE_BOOT_COMPLETED");
            }

            manifestDoc.Save(manifestPath);
        }

        internal static void InjectReceivers(XmlDocument manifestXmlDoc)
        {
            const string kNotificationManagerName = "com.unity.androidnotifications.UnityNotificationManager";
            const string kNotificationRestartOnBootName = "com.unity.androidnotifications.UnityNotificationRestartOnBootReceiver";

            var applicationXmlNode = manifestXmlDoc.SelectSingleNode("manifest/application");
            if (applicationXmlNode == null)
                return;

            XmlElement notificationManagerReceiver = null;
            XmlElement notificationRestartOnBootReceiver = null;

            // Search for existing receivers.
            foreach (XmlNode node in applicationXmlNode.ChildNodes)
            {
                var element = node as XmlElement;
                if (element == null || node.Name != "receiver")
                    continue;

                var elementName = element.GetAttribute("name", kAndroidNamespaceURI);
                if (elementName == kNotificationManagerName)
                    notificationManagerReceiver = element;
                else if (elementName == kNotificationRestartOnBootName)
                    notificationRestartOnBootReceiver = element;

                if (notificationManagerReceiver != null && notificationRestartOnBootReceiver != null)
                    break;
            }

            // Create notification manager receiver if necessary.
            if (notificationManagerReceiver == null)
            {
                notificationManagerReceiver = manifestXmlDoc.CreateElement("receiver");
                notificationManagerReceiver.SetAttribute("name", kAndroidNamespaceURI, kNotificationManagerName);

                applicationXmlNode.AppendChild(notificationManagerReceiver);
            }
            notificationManagerReceiver.SetAttribute("exported", kAndroidNamespaceURI, "true");

            // Create notification restart-on-boot receiver if necessary.
            if (notificationRestartOnBootReceiver == null)
            {
                notificationRestartOnBootReceiver = manifestXmlDoc.CreateElement("receiver");
                notificationRestartOnBootReceiver.SetAttribute("name", kAndroidNamespaceURI, kNotificationRestartOnBootName);

                var intentFilterNode = manifestXmlDoc.CreateElement("intent-filter");

                var actionNode = manifestXmlDoc.CreateElement("action");
                actionNode.SetAttribute("name", kAndroidNamespaceURI, "android.intent.action.BOOT_COMPLETED");

                intentFilterNode.AppendChild(actionNode);
                notificationRestartOnBootReceiver.AppendChild(intentFilterNode);
                applicationXmlNode.AppendChild(notificationRestartOnBootReceiver);
            }
            notificationRestartOnBootReceiver.SetAttribute("enabled", kAndroidNamespaceURI, "false");
        }

        internal static void AppendAndroidPermissionField(XmlDocument xmlDoc, string name)
        {
            var manifestNode = xmlDoc.SelectSingleNode("manifest");
            if (manifestNode == null)
                return;

            foreach (XmlNode node in manifestNode.ChildNodes)
            {
                if (!(node is XmlElement) || node.Name != "uses-permission")
                    continue;

                var elementName = ((XmlElement)node).GetAttribute("name", kAndroidNamespaceURI);
                if (elementName == name)
                    return;
            }

            XmlElement metaDataNode = xmlDoc.CreateElement("uses-permission");
            metaDataNode.SetAttribute("name", kAndroidNamespaceURI, name);

            manifestNode.AppendChild(metaDataNode);
        }

        internal static void AppendAndroidMetadataField(XmlDocument xmlDoc, string name, string value)
        {
            var nodes = xmlDoc.SelectNodes("manifest/application/meta-data");

            if (nodes != null)
            {
                // Check if there is a 'meta-data' with the same name.
                foreach (XmlNode node in nodes)
                {
                    var element = node as XmlElement;
                    if (element == null)
                        continue;

                    var elementName = element.GetAttribute("name", kAndroidNamespaceURI);
                    if (elementName == name)
                    {
                        element.SetAttribute("value", kAndroidNamespaceURI, value);
                        return;
                    }
                }
            }

            var applicationNode = xmlDoc.SelectSingleNode("manifest/application");
            if (applicationNode == null)
                return;

            XmlElement metaDataNode = xmlDoc.CreateElement("meta-data");
            metaDataNode.SetAttribute("name", kAndroidNamespaceURI, name);
            metaDataNode.SetAttribute("value", kAndroidNamespaceURI, value);

            applicationNode.AppendChild(metaDataNode);
        }
    }
}
#endif
