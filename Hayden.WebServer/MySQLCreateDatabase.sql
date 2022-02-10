/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET NAMES utf8 */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

CREATE TABLE IF NOT EXISTS `boards` (
  `Id` smallint unsigned NOT NULL AUTO_INCREMENT,
  `ShortName` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `LongName` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `Category` varchar(255) NOT NULL,
  `IsNSFW` tinyint NOT NULL DEFAULT '0',
  `IsMultiImage` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`Id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `files` (
  `Id` int unsigned NOT NULL AUTO_INCREMENT,
  `BoardId` smallint unsigned NOT NULL,
  `Md5Hash` binary(16) NOT NULL,
  `Sha1Hash` binary(20) NOT NULL,
  `Sha256Hash` binary(32) NOT NULL,
  `Extension` varchar(4) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `ImageWidth` smallint unsigned DEFAULT NULL,
  `ImageHeight` smallint unsigned DEFAULT NULL,
  `Size` int unsigned NOT NULL,
  PRIMARY KEY (`Id`) USING BTREE,
  UNIQUE KEY `Md5Hash` (`Md5Hash`,`BoardId`),
  UNIQUE KEY `Sha1Hash` (`Sha1Hash`,`BoardId`),
  UNIQUE KEY `Sha256Hash` (`Sha256Hash`,`BoardId`),
  KEY `FK_files_boardid_idx` (`BoardId`),
  CONSTRAINT `FK_files_boardid` FOREIGN KEY (`BoardId`) REFERENCES `boards` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `file_mappings` (
  `BoardId` smallint unsigned NOT NULL,
  `PostId` bigint unsigned NOT NULL,
  `FileId` int unsigned NOT NULL,
  `Index` tinyint unsigned NOT NULL DEFAULT '0',
  `Filename` varchar(255) NOT NULL,
  `IsSpoiler` tinyint NOT NULL,
  `IsDeleted` tinyint NOT NULL,
  PRIMARY KEY (`BoardId`,`PostId`,`FileId`),
  KEY `FK_post_mappings_fileid` (`FileId`),
  KEY `FK_post_mappings_boardid` (`BoardId`,`PostId`),
  CONSTRAINT `FK_image_mappings_post` FOREIGN KEY (`BoardId`, `PostId`) REFERENCES `posts` (`BoardId`, `PostId`),
  CONSTRAINT `FK_post_mappings_fileid` FOREIGN KEY (`FileId`) REFERENCES `files` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `posts` (
  `BoardId` smallint unsigned NOT NULL,
  `PostId` bigint unsigned NOT NULL,
  `ThreadId` bigint unsigned NOT NULL,
  `ContentHtml` text CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci,
  `ContentRaw` text CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci,
  `Author` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `DateTime` datetime NOT NULL,
  `IsDeleted` tinyint NOT NULL,
  PRIMARY KEY (`BoardId`,`PostId`) USING BTREE,
  KEY `BoardId_ThreadId` (`BoardId`,`ThreadId`,`DateTime`) USING BTREE,
  KEY `posts_boardid_idx` (`BoardId`),
  CONSTRAINT `posts_boardid` FOREIGN KEY (`BoardId`) REFERENCES `boards` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC;

CREATE TABLE IF NOT EXISTS `posts_bak` (
  `BoardId` smallint unsigned NOT NULL DEFAULT '0',
  `Board` varchar(5) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `PostId` bigint unsigned NOT NULL DEFAULT '0',
  `ThreadId` bigint unsigned NOT NULL DEFAULT '0',
  `ContentHtml` text CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci,
  `ContentRaw` text,
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
  `BoardId` smallint unsigned NOT NULL,
  `ThreadId` bigint unsigned NOT NULL,
  `Title` varchar(255) DEFAULT NULL,
  `LastModified` datetime NOT NULL,
  `IsArchived` bit(1) NOT NULL,
  `IsDeleted` bit(1) NOT NULL,
  PRIMARY KEY (`BoardId`,`ThreadId`) USING BTREE,
  KEY `LastModified` (`LastModified`),
  CONSTRAINT `threads_boardid` FOREIGN KEY (`BoardId`) REFERENCES `boards` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;
/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;
