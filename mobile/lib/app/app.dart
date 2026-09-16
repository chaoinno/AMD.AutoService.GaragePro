import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'navigation.dart';
import 'router.dart';
import 'theme.dart';

class GarageProApp extends ConsumerWidget {
  const GarageProApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => MaterialApp.router(
        title: 'GaragePro Service Ops',
        debugShowCheckedModeBanner: false,
        theme: buildAppTheme(),
        scaffoldMessengerKey: scaffoldMessengerKey,
        routerConfig: ref.watch(routerProvider),
      );
}
