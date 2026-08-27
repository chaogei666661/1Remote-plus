using System.ComponentModel;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Utils;
using Newtonsoft.Json;

namespace _1RM.View.Editor.Forms
{
    public class ProtocolBaseFormViewModel : NotifyPropertyChangedBaseScreen, IDataErrorInfo
    {
        public ProtocolBase New { get; }
        public ProtocolBaseFormViewModel(ProtocolBase protocolBase)
        {
            New = protocolBase;
            New.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ProtocolBase.SelectedRunnerName))
                    RaisePropertyChanged(nameof(SelectedRunnerIsInternalRunner));
            };
        }

        /// <summary>
        /// Drives the editor rows that only make sense for the built-in host — the PuTTY session file, the
        /// SSH startup command, the SFTP start path. It lives here rather than on the protocol because
        /// answering it needs <see cref="ProtocolConfigurationService"/>, and the model has no business
        /// resolving services.
        /// </summary>
        [JsonIgnore]
        public bool SelectedRunnerIsInternalRunner => New.IsSelectedRunnerInternal(IoC.Get<ProtocolConfigurationService>());

        ~ProtocolBaseFormViewModel()
        {
        }

        public virtual bool CanSave()
        {
            if (!string.IsNullOrEmpty(New[nameof(New.DisplayName)]))
                return false;
            return true;
        }

        #region IDataErrorInfo
        [JsonIgnore] public string Error => "";

        [JsonIgnore]
        public virtual string this[string columnName] => New[columnName];

        #endregion
    }
}
