/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET NAMES utf8 */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

CREATE TABLE IF NOT EXISTS `posts` (
  `Board` varchar(5) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `PostId` bigint unsigned NOT NULL DEFAULT '0',
  `ThreadId` bigint unsigned NOT NULL DEFAULT '0',
  `Html` text,
  `Author` varchar(255) DEFAULT NULL,
  `MediaHash` binary(16) DEFAULT NULL,
  `MediaFilename` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `DateTime` datetime NOT NULL,
  `IsSpoiler` bit(1) NOT NULL DEFAULT b'0',
  `IsDeleted` bit(1) NOT NULL,
  `IsImageDeleted` bit(1) NOT NULL,
  PRIMARY KEY (`Board`,`PostId`),
  KEY `Board_ThreadId` (`Board`,`ThreadId`,`DateTime`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `threads` (
  `Board` varchar(5) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `ThreadId` bigint unsigned NOT NULL DEFAULT '0',
  `Title` varchar(255) DEFAULT NULL,
  `LastModified` datetime NOT NULL,
  `IsArchived` bit(1) NOT NULL,
  `IsDeleted` bit(1) NOT NULL,
  PRIMARY KEY (`Board`,`ThreadId`),
  KEY `LastModified` (`LastModified`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;
/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;
