import json, sys
sys.path.insert(0, 'tools')
from info_texts import T, APPX
p = 'WinGuard/Data/settings.json'
L = json.load(open(p, encoding='utf-8'))
for s in L:
    if s['id'] == 'widgets':
        s['name'] = 'Uninstall Widgets (Windows Web Experience Pack)'
        s['nameFr'] = 'Désinstaller les widgets (Windows Web Experience Pack)'
        s['description'] = 'Removes the widgets board, its news feed and taskbar weather. Undo reinstalls it from the Store.'
        s['descriptionFr'] = "Supprime le tableau de widgets, son flux d'actualités et la météo de la barre des tâches. L'annulation le réinstalle depuis le Store."
        s['recommendation'] = 'LimitedRecommended'
        s['actions'] = [{"type": "Appx", "target": "MicrosoftWindows.Client.WebExperience", "storeId": "9MSSGKG348SP"}]
RISK = {
 'Recommended': ("Recommandé : gain de confidentialité réel, aucun effet gênant attendu pour un usage normal.",
                 "Recommended: real privacy gain, no annoying side effect expected for normal use."),
 'LimitedRecommended': ("Recommandé avec limites : utile, mais peut désactiver une fonction que tu utilises (voir contreparties) ; à choisir selon ton usage.",
                        "Limited: useful, but may turn off a feature you use (see trade-offs); pick it based on your usage."),
 'NotRecommended': ("Déconseillé : le gain est faible ou la contrepartie importante (sécurité, fonction perdue, réinstallation manuelle). À n'activer qu'en connaissance de cause.",
                    "Not recommended: small gain or significant trade-off (security, lost feature, manual reinstall). Only enable if you know why."),
}
def act(a, fr):
    t = a['type']
    if t == 'Registry':
        v = a.get('disabledValue')
        k = f"{a['hive']}\\{a['path']}\\{a['name']} = {a['enabledValue']} ({a.get('kind','DWord')})"
        if fr: return f"• Registre : {k}. Annulation : " + (f"remis à {v}." if v is not None else "valeur supprimée (défaut Windows).")
        return f"• Registry: {k}. Undo: " + (f"set back to {v}." if v is not None else "value deleted (Windows default).")
    if t == 'ScheduledTask':
        names = ' / '.join(a['target'].split('|'))
        return (f"• Tâche planifiée désactivée : {names}. Annulation : réactivée seulement si WinGuard l'a désactivée." if fr
                else f"• Scheduled task disabled: {names}. Undo: re-enabled only if WinGuard disabled it.")
    if t == 'Appx':
        sid = a.get('storeId')
        if fr: return f"• Paquet supprimé pour tous les utilisateurs : {a['target']} (Remove-AppxPackage -AllUsers). Annulation : " + (f"réinstallation depuis le Store (winget, ID {sid}) ; si ça échoue, WinGuard te propose d'ouvrir le Store." if sid else "réinstallation MANUELLE depuis le Microsoft Store (pas d'ID Store automatique).") + " Si l'appli n'était pas installée, rien n'est fait (badge « Non installé »)."
        return f"• Package removed for all users: {a['target']} (Remove-AppxPackage -AllUsers). Undo: " + (f"reinstall from the Store (winget, ID {sid}); if it fails WinGuard offers to open the Store." if sid else "MANUAL reinstall from the Microsoft Store (no automatic Store ID).") + " If the app wasn't installed, nothing is done ('Not installed' badge)."
    if t == 'Hosts':
        n = len(a['target'].split('|'))
        return (f"• Fichier hosts : {n} domaines redirigés vers 0.0.0.0 dans un bloc marqué WinGuard. Annulation : seul ce bloc est retiré." if fr
                else f"• Hosts file: {n} domains pointed to 0.0.0.0 in a WinGuard-marked block. Undo: only that block is removed.")
    if t == 'Firewall':
        return (f"• Pare-feu : règle sortante « Bloquer » (groupe WinGuard) pour {a['target']}. Annulation : règle supprimée." if fr
                else f"• Firewall: outbound 'Block' rule (WinGuard group) for {a['target']}. Undo: rule deleted.")
missing = []
for s in L:
    i = s['id']
    if i in T: efr, een = T[i]
    elif i in APPX:
        efr = "Désinstalle " + APPX[i][0]; een = "Uninstalls " + APPX[i][1]
    else: missing.append(i); continue
    rf, re_ = RISK[s['recommendation']]
    scope_fr = "ton compte uniquement" if s['scope'] == 'Current User' and not any(a['type']=='Appx' for a in s['actions']) else "tout le PC (tous les comptes)"
    scope_en = "your account only" if scope_fr.startswith("ton") else "the whole PC (all accounts)"
    s['infoFr'] = "Quand c'est activé :\n" + efr + "\n\nCe qui est modifié — " + scope_fr + " :\n" + "\n".join(act(a, True) for a in s['actions']) + "\n\nNiveau de risque :\n" + rf
    s['info'] = "When enabled:\n" + een + "\n\nWhat changes — " + scope_en + ":\n" + "\n".join(act(a, False) for a in s['actions']) + "\n\nRisk level:\n" + re_
json.dump(L, open(p, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
print("settings:", len(L), "with info:", sum(1 for s in L if s.get('info') and s.get('infoFr')), "missing:", missing)
