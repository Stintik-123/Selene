"""Dev setup (run with Playnite closed): load Selene Core from SeleneCore\\bin via Playnite's developer extension list,
and give it this PC's settings. Personal values are passed on the command line, never stored in the repo."""
import json, os, sys

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bin_dir = os.path.join(root, 'SeleneCore', 'bin')
cfg_path = os.path.expandvars(r'%APPDATA%\Playnite\config.json')

cfg = json.load(open(cfg_path, encoding='utf-8-sig'))
dev = cfg.get('DevelExtenions') or []
if not any(isinstance(d, dict) and os.path.normcase(d.get('Item', '')) == os.path.normcase(bin_dir) for d in dev):
    dev.append({'Item': bin_dir, 'Selected': True})
cfg['DevelExtenions'] = dev
json.dump(cfg, open(cfg_path, 'w', encoding='utf-8'), indent=2)
print('developer extensions:', [d.get('Item') for d in dev if isinstance(d, dict)])

# usage: register_dev.py [media folder] [game folder;game folder] [helper exe] [stream logs folder]
args = sys.argv[1:] + [''] * 4
settings = {'MediaFolder': args[0], 'GameFolders': [g for g in args[1].split(';') if g], 'HelperPath': args[2], 'StreamLogsFolder': args[3]}
data_dir = os.path.expandvars(r'%APPDATA%\Playnite\ExtensionsData\cd424c29-97f4-4dd0-aa66-4d8e01cc8724')
os.makedirs(data_dir, exist_ok=True)
json.dump(settings, open(os.path.join(data_dir, 'config.json'), 'w', encoding='utf-8'), indent=2)
print('settings:', settings)
