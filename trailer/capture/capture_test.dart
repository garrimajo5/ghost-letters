// Run from client/: flutter test ../trailer/capture/capture_test.dart
// Real production widgets, deterministic staged data (brief, method B).
import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/app.dart';
import 'package:ghost_letters/core/api.dart';
import 'package:ghost_letters/core/app_version.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:ghost_letters/core/realtime.dart';
import 'package:ghost_letters/core/session.dart';
import 'package:ghost_letters/core/sound.dart';
import 'package:ghost_letters/models/models.dart';
import '../../client/test/support/fakes.dart';
import '../../client/test/support/fixtures.dart' show snapshotJson;

const names = ['Бот Пуаро', 'Бот Ватсон', 'Бот Марпл', 'Бот Коломбо', 'Бот Фандорин', 'Бот Жеглов'];
const colors = ['#5C7C99', '#48A1D6', '#B16ADE', '#ED854A', '#E3A94B', '#3D6A99'];
const descriptions = ['Смысл прежде всего. Упрям, мало врёт.', 'Слушает большинство и ищет компромисс.', 'Помнит всех и всё. Верит людям.', 'Обвиняет в лоб и рискует.', 'Видит цвета. Холодный расчёт.', 'Блефует на любой роли. Никому не верит.'];
User user(int i) => User(id: 'u${i+1}', nickname: names[i], avatarColor: colors[i]);
const board = [
  ['orig_0401', 'orig_0716', 'orig_0002', 'orig_0097', 'orig_0478'],
  ['orig_0073', 'orig_0186', 'orig_0206', 'orig_0125', 'orig_0082'],
  ['orig_0175', 'orig_0407', 'orig_0040', 'orig_0229', 'orig_0557'],
  ['orig_0264', 'orig_0243', 'orig_0061', 'orig_0271', 'orig_0331'],
];
const hand = ['orig_0323', 'orig_0300', 'orig_0058', 'orig_0263', 'orig_0061'];

class CaptureApi extends FakeApi {
  CaptureApi(super.ref);
  @override
  Future<Profile> profile(String id) async => Profile(user: user(1), games: 24, wins: 16, rating: 1248, likes: 19, achievements: const []);
  @override
  Future<List<LeaderRow>> leaderboard({bool bots = false}) async => [for (var i=0;i<6;i++) LeaderRow(user: user(i), rating: 1380-i*43, games: 42-i*3, wins: 30-i*3, isBot: true)];
}

GameSnapshot shot(int c, {int version=42}) {
  final phase = {3:'RoleReveal',4:'Night',6:'Mailbox',7:'GhostPick',12:'Voting',13:'Voting',14:'Hunt',15:'Finished'}[c] ?? 'Discussion';
  final allowed = {3:['AckRole'],4:['ChooseTruth'],6:['SendLetter'],7:['RevealHints'],12:['CastVote'],13:['CastVote'],14:['HuntPick'],15:['Like']}[c] ?? ['EndTurn','GiveFloor'];
  final j=snapshotJson(phase:phase,allowed:allowed);final v=j['view'] as Json;
  v['version']=version;v['round']=3;v['board']=[for(var r=0;r<4;r++) {'category':['Method','Motive','Place','Secret'][r],'cards':board[r]}];
  v['players']=[for(var i=0;i<6;i++) {'id':'u${i+1}','seat':i,'isGhost':i==0,'knownRole':c==15?['Ghost','Detective','Killer','Witness','Accomplice','Detective'][i]:(i==0?'Ghost':null),'hasActed':i==1,'handCount':5}];
  v['me']={'id':'u2','role':c==3||c==4||c==14?'Killer':c==7?'Ghost':'Detective','hand':hand,'letters':<Json>[]};
  v['hints']=[{'round':1,'cards':['orig_0058','orig_0323']},{'round':2,'cards':['orig_0243','orig_0300']}];
  v['currentSpeaker']='u2';v['raisedHands']=['u3'];v['vanishedCount']=4;
  v['teamSuggestions']=c==4?[{'from':'u5','columns':[2,1,3,0],'target':null,'guess':null}]:<Json>[];
  v['huntRoles']=['Witness'];
  if(c==7){v['mailboxForGhost']=hand;v['mailboxCount']=5;}
  if(c==5) {v['me']=null;v['allowedCommands']=<String>[];}
  final f=v['finale'] as Json;
  f['currentStage']=(c==12||c==13)?{'index':c==12?0:4,'kind':c==12?'Row':'Killer','row':c==12?0:-1,'attempt':1,'candidateColumns':[0,1,2,3,4],'candidateSuspects':[for(var i=2;i<=6;i++) 'u$i']}:null;
  f['votes']=c==12?[{'stage':0,'attempt':1,'voter':'u3','column':2,'suspect':null},{'stage':0,'attempt':1,'voter':'u4','column':2,'suspect':null}]:<Json>[];
  if(c==15){v['truth']=[2,1,3,0];f['result']={'solved':true,'correctRows':4,'killerCaught':true,'side':'Detectives','imitatorWon':false,'blackmailerWon':false,'winners':['u1','u2','u4','u6'],'blackmailerClaim':null};}
  if(c==8&&version>42){v['hints']=[...(v['hints'] as List),{'round':3,'cards':['orig_0264']}];v['vanishedCount']=5;}
  if(c==6&&version>42){v['allowedCommands']=<String>[];v['mailboxCount']=4;(v['me'] as Json)['hand']=hand.skip(1).toList();(v['me'] as Json)['letters']=[{'round':3,'cardId':'orig_0323','revealed':false}];}
  if(c==12&&version>42){f['myVote']={'column':2,'suspect':null};f['votes']=[...(f['votes'] as List),{'stage':0,'attempt':1,'voter':'u2','column':2,'suspect':null}];}
  j['deadline']=null;j['roster']=[for(var i=0;i<6;i++) {'id':'u${i+1}','nickname':names[i],'avatarColor':colors[i],'seat':i}];
  return GameSnapshot.fromJson(j);
}

class CaptureApp {
  CaptureApp(this.container);
  final ProviderContainer container;
  CaptureApi get api=>container.read(apiProvider) as CaptureApi;
  FakeRealtime get realtime=>container.read(realtimeProvider) as FakeRealtime;
  Widget get widget=>UncontrolledProviderScope(container:container,child:const GhostLettersApp());
  void go(String route)=>container.read(routerProvider).go(route);
}
void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    final icons=FontLoader('MaterialIcons')..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));await icons.load();
    for(final family in ['Oswald','GolosText']) {
      final loader=FontLoader(family);
      for(final weight in [400,500,600,700]) {loader.addFont(rootBundle.load('assets/fonts/$family-$weight.ttf'));}
      await loader.load();
    }
  });
  for(var c=1;c<=18;c++) {
    testWidgets('Capture C$c', (tester) async {
      final wide=c==5||c==18;final size=wide?const Size(1366,1024):const Size(412,915);final ratio=wide?2.0:3.0;
      tester.view.devicePixelRatio=ratio;tester.view.physicalSize=Size(size.width*ratio,size.height*ratio);
      addTearDown(tester.view.reset);
      SharedPreferences.setMockInitialValues({'session':jsonEncode({'user':user(1).toJson(),'accessToken':'capture','refreshToken':'capture'})});
      final prefs=await SharedPreferences.getInstance();
      final container=ProviderContainer(overrides:[prefsProvider.overrideWithValue(prefs),apiProvider.overrideWith(CaptureApi.new),realtimeProvider.overrideWith(FakeRealtime.new),soundOutputProvider.overrideWith((ref)=>FakeSoundOutput()),latestAndroidVersionProvider.overrideWith((ref)async=>null)]);
      final app=CaptureApp(container);
      final api=app.api;
      api.botList=[for(var i=0;i<6;i++) BotInfo(id:'b$i',nickname:names[i],avatarColor:colors[i],about:descriptions[i],spectra:const BotSpectra(),enabled:true)];
      final l=Lobby.fromJson({'id':'l1','code':'MIST26','title':'Ночь в особняке','hostUserId':'u2','status':'open','settings':LobbySettings(useSecretRow:true,ghostUserId:'u1',roles:const RoleOptions(killerEnabled:true,useWitness:true)).toJson(),'members':[for(var i=0;i<6;i++) {'userId':'u${i+1}','nickname':names[i],'avatarColor':colors[i],'seat':i,'mode':'player','isReady':true,'isBot':true}]});
      api.lobbyResult=l;app.realtime.lobby=l;api.snapshotResult=shot(c);app.realtime.game=shot(c);
      final key=GlobalKey();
      await tester.pumpWidget(RepaintBoundary(key:key,child:app.widget));await tester.pumpAndSettle();
      app.go(c==1?'/':c==2?'/lobby/l1':c==16?'/leaderboard':c==17?'/profile/u2':'/game/g1');await tester.pumpAndSettle();
      final ctx=tester.element(find.byType(MaterialApp));
      await tester.runAsync(() async {
        final manifest=await AssetManifest.loadFromAssetBundle(rootBundle);
        final wanted=manifest.listAssets().where((a)=>(a.startsWith('assets/images/')&&a.endsWith('.webp'))||[...board.expand((r)=>r),...hand,'orig_0243'].any((id)=>a.endsWith('$id.webp')));
        await Future.wait(wanted.map((a)=>precacheImage(AssetImage(a),ctx)));
        await Future<void>.delayed(const Duration(milliseconds:400));
      });await tester.pumpAndSettle();
      Future<void> tap(Finder f) async {if(f.evaluate().isNotEmpty){await tester.ensureVisible(f.first);await tester.pumpAndSettle();await tester.tap(f.first);await tester.pumpAndSettle();}}
      Future<void> save(String name) async {
        if(!name.contains('motion')) {await tester.runAsync(() async {await Future<void>.delayed(const Duration(milliseconds:300));});}
        await tester.pump();
        final b=key.currentContext!.findRenderObject()! as RenderRepaintBoundary;
        await tester.runAsync(() async {final image=await b.toImage(pixelRatio:ratio);final png=await image.toByteData(format:ui.ImageByteFormat.png);final file=File('../trailer/captures/$name.png');await file.parent.create(recursive:true);await file.writeAsBytes(png!.buffer.asUint8List());image.dispose();});
      }
      if(c==2){await save('C2-lobby');await tester.drag(find.byType(Scrollable).first,const Offset(0,-430));await tester.pumpAndSettle();await tap(find.byKey(const Key('add-bot')));}
      if(c==3){await tap(find.byKey(const Key('role-card')));}
      if(c==4){for(var r=0;r<4;r++){await tester.longPress(find.byKey(Key('board-$r-${[2,1,3,0][r]}')));await tester.pump(const Duration(milliseconds:400));}}
      if(c==7){await tap(find.byKey(const Key('mailbox-orig_0323')));await tap(find.byKey(const Key('mailbox-orig_0058')));}
      if(c==9){
        for(final m in [
          {'id':'m1','authorId':'u3','kind':'text','text':'Ключ и часы — подсказка к тайне.','cardIds':['orig_0264','orig_0243']},
          {'id':'m2','authorId':'u4','kind':'voice','text':null,'mediaId':'demo','durationMs':12000,'cardIds':<String>[]},
          {'id':'m3','authorId':'u6','kind':'text','text':'Думаю, Убийца — Бот Фандорин.','cardIds':<String>[]},
        ]) {app.realtime.chatCtl.add(ChatMessage.fromJson({...m,'channel':'public','createdAt':'2026-10-09T19:02:00Z','round':3}));}
        await tester.pumpAndSettle();await save('C9-radio');await tap(find.byTooltip('Чат'));
      }
      if(c==10){await tap(find.byKey(const Key('board-0-2')));await tap(find.byKey(const Key('mark-believed')));}
      if(c==11){await tap(find.byKey(const Key('zoom-board')));}
      if(c==13||c==14){await tap(find.byKey(const Key('player-u3')));}
      if(c==16){await tap(find.byKey(const Key('show-bots')));}
      await save('C$c');
      if([6,8,11,12].contains(c)) {
        // Capture actual widget animation/state changes, not a simulated interface.
        for(var frame=0;frame<48;frame++) {
          if(frame==12){
            if(c==6) {await tap(find.byKey(const Key('hand-orig_0323')));}
            if(c==8) {final next=shot(8,version:43);app.realtime.viewsCtl.add((view:next.view,deadline:null));}
            if(c==11) {await tester.tap(find.byKey(const Key('zoom-in')));}
            if(c==12) {await tap(find.byKey(const Key('board-0-2')));}
          }
          if(frame==24&&(c==6||c==12)){await tap(find.byKey(const Key('cta')));final next=shot(c,version:43);app.realtime.viewsCtl.add((view:next.view,deadline:null));}
          await tester.pump(const Duration(microseconds:83333));
          await save('motion/C$c/${frame.toString().padLeft(3,'0')}');
        }
      }
      await tester.pumpWidget(const SizedBox());await tester.pump();app.container.dispose();
      expect(tester.takeException(),isNull);
    });
  }
}
