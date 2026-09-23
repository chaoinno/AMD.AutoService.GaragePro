/// path ทั้งหมดของแอป — อ้างอิงจากที่นี่เท่านั้น ห้ามพิมพ์ string ซ้ำในหน้าอื่น
abstract final class Routes {
  static const login = '/auth/login';

  static const home = '/home';
  static const jobs = '/jobs';
  static const approval = '/approval';
  static const reports = '/reports';
  static const newJob = '/intake/new';
  static const profile = '/profile';

  static String job(String jobId) => '/jobs/$jobId';
  static String jobChat(String jobId) => '/jobs/$jobId/chat';
  static String jobIntake(String jobId) => '/jobs/$jobId/intake';
  static String jobQc(String jobId) => '/jobs/$jobId/qc';
  static String jobPayment(String jobId) => '/jobs/$jobId/payment';
  static String jobHandover(String jobId) => '/jobs/$jobId/handover';
  static String quotationApproval(String quotationId) => '/approval/$quotationId';
}
